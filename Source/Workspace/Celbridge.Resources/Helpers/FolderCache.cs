using System.Collections.Concurrent;
using System.IO.Enumeration;
using System.IO.Hashing;
using System.Runtime.InteropServices;

namespace Celbridge.Resources.Helpers;

/// <summary>
/// The outcome of looking a name up in a folder's listing.
/// </summary>
internal enum NameLookup
{
    Found,
    Missing,
    Unlisted,
}

/// <summary>
/// A folder the resolver has checked. Only FolderCache reads or changes its state. CachedFolder is a class, not a
/// record, because the cache compares entries by reference, locks on them and changes their state.
/// </summary>
internal sealed class CachedFolder
{
    public CachedFolder(string folderPath, CachedFolder? parent, DateTimeOffset cachedAt)
    {
        FolderPath = folderPath;
        Parent = parent;
        CachedAt = cachedAt;
    }

    public string FolderPath { get; }

    public CachedFolder? Parent { get; }

    public DateTimeOffset CachedAt { get; }

    // Changed through Interlocked.
    public int ChildFolderCount;

    // Read and written under the lock on the folder.
    public HashSet<ulong>? NameHashes;
}

/// <summary>
/// The folders a root's path resolver has checked, each with the hashes of the names stored in it. Each cached folder
/// exists, and the resolver has checked the folder and every folder above for links. Under a listing policy, the
/// folder's path uses the names stored on disk. The cache stays current through the creates and removals recorded with
/// it, and the listing policy decides how long a folder stays cached.
/// </summary>
[AllowDirectFileSystemAccess]
internal sealed class FolderCache
{
    // Safety valves for a session that touches an unbounded number of folders or names. A folder costs one
    // check and one listing to rebuild, so clearing wholesale is enough.
    internal const int DefaultMaxCachedFolders = 100_000;
    internal const int DefaultMaxListedNames = 1_000_000;

    // How long a folder stays cached under the Expiring listing policy.
    private static readonly TimeSpan ExpiringFolderLifetime = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The options for every listing of a folder's names. A listing includes the hidden and system items that the
    /// default options skip. A folder that cannot be read fails the listing.
    /// </summary>
    internal static readonly EnumerationOptions ListingOptions = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
    };

    private readonly string _backingFolderPath;
    private readonly ListingPolicy _listingPolicy;
    private readonly TimeProvider _timeProvider;
    private readonly int _maxCachedFolders;
    private readonly int _maxListedNames;

    // Resolution runs on the scanner's parallel workers as well as the tree walk. The cache compares paths with
    // PathComparison, so a change recorded in any letter case finds the folder. A lookup also compares the cached
    // folder's path exactly. A key that differs from a cached folder's stored path is then checked again. The
    // counts are tracked separately because ConcurrentDictionary.Count locks every bucket.
    private readonly ConcurrentDictionary<string, CachedFolder> _cachedFolders;
    private int _cachedFolderCount;
    private int _listedNameCount;
    private int _listingCount;

    public FolderCache(
        string backingFolderPath,
        ListingPolicy listingPolicy,
        TimeProvider timeProvider,
        int maxCachedFolders = DefaultMaxCachedFolders,
        int maxListedNames = DefaultMaxListedNames)
    {
        _backingFolderPath = backingFolderPath;
        _listingPolicy = listingPolicy;
        _timeProvider = timeProvider;
        _maxCachedFolders = maxCachedFolders;
        _maxListedNames = maxListedNames;
        _cachedFolders = new ConcurrentDictionary<string, CachedFolder>(PathComparison.Comparer);
    }

    /// <summary>
    /// Returns true if the cache keeps listings, so a lookup can check a name against the names stored on disk.
    /// </summary>
    public bool HasListings => _listingPolicy != ListingPolicy.None;

    /// <summary>
    /// Returns the number of folders in the cache.
    /// </summary>
    public int Count => _cachedFolders.Count;

    /// <summary>
    /// Returns the number of folder listings the cache has read.
    /// </summary>
    public int ListingCount => Volatile.Read(ref _listingCount);

    /// <summary>
    /// Returns the cached folder at the path when the path matches the folder's stored path exactly. An expired
    /// folder is dropped, with every folder below.
    /// </summary>
    public CachedFolder? TryGet(string folderPath)
    {
        if (!_cachedFolders.TryGetValue(folderPath, out var folder)
            || !string.Equals(folder.FolderPath, folderPath, StringComparison.Ordinal))
        {
            return null;
        }

        if (_listingPolicy == ListingPolicy.Expiring
            && _timeProvider.GetUtcNow() - folder.CachedAt > ExpiringFolderLifetime)
        {
            if (_cachedFolders.TryRemove(KeyValuePair.Create(folder.FolderPath, folder)))
            {
                DropRemovedFolder(folder);
            }

            return null;
        }

        return folder;
    }

    /// <summary>
    /// Caches a folder that the resolver has checked, below its parent. The path in another letter case may be
    /// cached already, as for sibling folders that differ only in case. The folder is then returned uncached, and
    /// its names are listed on each use.
    /// </summary>
    public CachedFolder Add(string folderPath, CachedFolder? parent)
    {
        var folder = new CachedFolder(folderPath, parent, _timeProvider.GetUtcNow());
        if (!_cachedFolders.TryAdd(folderPath, folder))
        {
            return TryGet(folderPath) ?? folder;
        }

        if (parent is not null)
        {
            Interlocked.Increment(ref parent.ChildFolderCount);
        }

        // The count drifts when threads cache and drop folders at once. The valve then trips early, which
        // costs one check per folder.
        if (Interlocked.Increment(ref _cachedFolderCount) >= _maxCachedFolders)
        {
            Clear();
        }

        return folder;
    }

    /// <summary>
    /// Looks the name up in the folder's listing. A name missing from the listing may belong to an item created
    /// outside Celbridge after the listing. The folder is then listed again before the name counts as missing.
    /// Lookups that arrive together wait on the lock and share one listing.
    /// </summary>
    public NameLookup FindName(CachedFolder folder, string name)
    {
        var nameHash = HashName(name);

        lock (folder)
        {
            if (folder.NameHashes is not null
                && folder.NameHashes.Contains(nameHash))
            {
                return NameLookup.Found;
            }

            if (!TryListFolder(folder))
            {
                return NameLookup.Unlisted;
            }

            if (folder.NameHashes!.Contains(nameHash))
            {
                return NameLookup.Found;
            }

            return NameLookup.Missing;
        }
    }

    /// <summary>
    /// Records that an item now exists at the full path, which is under the backing folder. The item's name joins
    /// the listing of the folder that contains the item. A new folder above the item joins the listing of the
    /// folder above the new one.
    /// </summary>
    public void RecordItemCreated(string itemPath)
    {
        if (!HasListings)
        {
            return;
        }

        // A cached folder exists, so the listing of its parent already has the folder's name. The folders below
        // the first cached folder may be new. Only the outermost new folder has a listing to join.
        var currentPath = itemPath;
        while (!currentPath.Equals(_backingFolderPath, PathComparison.Comparison))
        {
            var folderPath = Path.GetDirectoryName(currentPath);
            if (folderPath is null)
            {
                return;
            }

            var folder = TryGet(folderPath);
            if (folder is not null)
            {
                AddName(folder, Path.GetFileName(currentPath));
                return;
            }

            currentPath = folderPath;
        }
    }

    /// <summary>
    /// Records that the item at the full path, which is under the backing folder, is gone. The item's name leaves
    /// the listing of the folder that contained the item. Every cached folder at or below the path is dropped too.
    /// A cached folder skips the link check, so a link created at one of those paths later would otherwise go
    /// unnoticed. The item may have been a file or a folder.
    /// </summary>
    public void RecordItemRemoved(string itemPath)
    {
        if (itemPath.Equals(_backingFolderPath, PathComparison.Comparison))
        {
            Clear();
            return;
        }

        DropFolder(itemPath);

        if (!HasListings)
        {
            return;
        }

        var folderPath = Path.GetDirectoryName(itemPath);
        if (folderPath is null)
        {
            return;
        }

        var folder = TryGet(folderPath);
        if (folder is not null)
        {
            RemoveName(folder, Path.GetFileName(itemPath));
        }
    }

    /// <summary>
    /// Drops every cached folder and listing.
    /// </summary>
    public void Clear()
    {
        _cachedFolders.Clear();
        Interlocked.Exchange(ref _cachedFolderCount, 0);
        Interlocked.Exchange(ref _listedNameCount, 0);
    }

    // Reads the folder's names into the folder's listing. The caller has locked the folder.
    private bool TryListFolder(CachedFolder folder)
    {
        HashSet<ulong> nameHashes;
        try
        {
            var listing = new FileSystemEnumerable<ulong>(
                folder.FolderPath,
                (ref System.IO.Enumeration.FileSystemEntry entry) => HashName(entry.FileName),
                ListingOptions);
            nameHashes = new HashSet<ulong>(listing);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        Interlocked.Increment(ref _listingCount);

        var previousNameCount = folder.NameHashes?.Count ?? 0;
        folder.NameHashes = nameHashes;

        // The lookup in progress still uses this listing. The folder is listed again on the next lookup.
        if (Interlocked.Add(ref _listedNameCount, nameHashes.Count - previousNameCount) > _maxListedNames)
        {
            Clear();
        }

        return true;
    }

    private void AddName(CachedFolder folder, string name)
    {
        var nameHash = HashName(name);

        lock (folder)
        {
            if (folder.NameHashes is not null
                && folder.NameHashes.Add(nameHash))
            {
                Interlocked.Increment(ref _listedNameCount);
            }
        }
    }

    private void RemoveName(CachedFolder folder, string name)
    {
        var nameHash = HashName(name);

        lock (folder)
        {
            if (folder.NameHashes is not null
                && folder.NameHashes.Remove(nameHash))
            {
                Interlocked.Decrement(ref _listedNameCount);
            }
        }
    }

    // Hashes the name's UTF-16 code units to 64 bits. When two names share a hash, the check cannot tell them
    // apart, and a key that differs from the stored name passes. With a million names cached, this happens to
    // about one lookup in 10^13.
    private static ulong HashName(ReadOnlySpan<char> name)
    {
        return XxHash3.HashToUInt64(MemoryMarshal.AsBytes(name));
    }

    // Drops the cached folder at the path, in any letter case, and every cached folder below it.
    private void DropFolder(string folderPath)
    {
        if (_cachedFolders.TryRemove(folderPath, out var folder))
        {
            DropRemovedFolder(folder);
        }
    }

    // Drops every cached folder below a folder just removed from the cache. A folder's parent is cached before
    // the folder. A scan is needed only when the removed folder has cached children.
    private void DropRemovedFolder(CachedFolder folder)
    {
        ForgetFolder(folder);

        if (folder.Parent is not null)
        {
            Interlocked.Decrement(ref folder.Parent.ChildFolderCount);
        }

        if (Volatile.Read(ref folder.ChildFolderCount) <= 0)
        {
            return;
        }

        var descendantPrefix = folder.FolderPath + Path.DirectorySeparatorChar;
        foreach (var entry in _cachedFolders)
        {
            if (entry.Key.StartsWith(descendantPrefix, PathComparison.Comparison)
                && _cachedFolders.TryRemove(entry))
            {
                ForgetFolder(entry.Value);
            }
        }
    }

    private void ForgetFolder(CachedFolder folder)
    {
        Interlocked.Decrement(ref _cachedFolderCount);

        int nameCount;
        lock (folder)
        {
            nameCount = folder.NameHashes?.Count ?? 0;
        }

        Interlocked.Add(ref _listedNameCount, -nameCount);
    }
}
