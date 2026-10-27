using System.Collections.Concurrent;
using System.IO.Enumeration;
using System.IO.Hashing;
using System.Runtime.InteropServices;

namespace Celbridge.Resources.Helpers;

/// <summary>
/// Counts the names in a folder cache's listings, so the cache can clear itself past a limit. The count drifts when
/// threads change listings at once. The limit tolerates the drift.
/// </summary>
internal sealed class ListedNameCounter
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Add(int change)
    {
        Interlocked.Add(ref _count, change);
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _count, 0);
    }
}

/// <summary>
/// The folders a root's path resolver has checked, each with the hashes of the names stored in it. Each cached folder
/// exists, and the resolver has checked the folder and every folder above for links. When the root keeps listings,
/// the folder's path uses the names stored on disk. The cache stays current through the creates and removals recorded with
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

    // Resolution runs on the scanner's parallel workers as well as the tree walk. The cache keys a path with
    // PathComparison, so the watcher finds a folder whatever letter case it reports the path in. Each key contains
    // the folders stored under the path, each with its exact path. There is one folder under a key, except on a
    // case-sensitive volume, where sibling folders can differ only in case. The write lock guards every change to the
    // dictionary, and a lookup takes no lock. The counts are tracked separately because ConcurrentDictionary.Count
    // locks every bucket.
    private readonly ConcurrentDictionary<string, CachedFolder[]> _cachedFolders;
    private readonly Lock _writeLock = new();
    private int _cachedFolderCount;
    private int _listingCount;

    private readonly ListedNameCounter _listedNames = new();
    private readonly ListingClock _listingClock = new();

    // Made once, since a lookup passes it to the folder on every call.
    private readonly Func<string, int, FolderListing?> _listFolder;

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
        _cachedFolders = new ConcurrentDictionary<string, CachedFolder[]>(PathComparison.Comparer);
        _listFolder = ListFolder;
    }

    /// <summary>
    /// Returns true if the cache keeps listings, so a lookup can check a name against the names stored on disk.
    /// </summary>
    public bool HasListings => _listingPolicy != ListingPolicy.None;

    /// <summary>
    /// Returns the number of folders in the cache. The count walks the cache, so tests read it and the valves use
    /// their own counts.
    /// </summary>
    public int Count => _cachedFolders.Values.Sum(folders => folders.Length);

    /// <summary>
    /// Returns the number of folder listings the cache has read.
    /// </summary>
    public int ListingCount => Volatile.Read(ref _listingCount);

    /// <summary>
    /// Returns the current timestamp of the cache's listings. A listing that starts from now on takes a later one.
    /// </summary>
    public ListingTimestamp GetListingTimestamp()
    {
        return _listingClock.Now();
    }

    /// <summary>
    /// Returns the cached folder stored under exactly the path. An expired folder is dropped, with every folder
    /// below.
    /// </summary>
    public CachedFolder? TryGet(string folderPath)
    {
        if (!_cachedFolders.TryGetValue(folderPath, out var folders))
        {
            return null;
        }

        var folder = FindExactPath(folders, folderPath);
        if (folder is null)
        {
            return null;
        }

        if (_listingPolicy == ListingPolicy.Expiring
            && _timeProvider.GetUtcNow() - folder.CachedAt > ExpiringFolderLifetime)
        {
            RemoveFolder(folder);
            return null;
        }

        return folder;
    }

    /// <summary>
    /// Caches a folder the resolver has checked, below its parent, and returns the cached folder. A folder cached
    /// already under the same path is returned instead. Nothing is released afterwards.
    /// </summary>
    public CachedFolder Acquire(string folderPath, CachedFolder? parent)
    {
        var folder = new CachedFolder(folderPath, parent, _timeProvider.GetUtcNow(), _listedNames);

        lock (_writeLock)
        {
            if (_cachedFolders.TryGetValue(folderPath, out var folders))
            {
                var cachedFolder = FindExactPath(folders, folderPath);
                if (cachedFolder is not null)
                {
                    return cachedFolder;
                }

                _cachedFolders[folderPath] = [.. folders, folder];
            }
            else
            {
                _cachedFolders[folderPath] = [folder];
            }
        }

        parent?.AddCachedChild();

        // The count drifts when threads cache and drop folders at once. The valve then trips early, which
        // costs one check per folder.
        if (Interlocked.Increment(ref _cachedFolderCount) >= _maxCachedFolders)
        {
            Clear();
        }

        return folder;
    }

    /// <summary>
    /// Looks the name up in the folder's listing. A name missing from a current listing may belong to an item created
    /// outside Celbridge after the listing. The folder is then listed again before the name counts as missing. A stale
    /// listing is listed again too.
    /// </summary>
    public NameLookup FindName(CachedFolder folder, string name)
    {
        return FindName(folder, name, acceptListingsAfter: null);
    }

    /// <summary>
    /// Looks the name up in the folder's listing. A listing started after the timestamp answers both ways, even when
    /// stale. Any other listing follows the rules of the lookup without a timestamp.
    /// </summary>
    public NameLookup FindName(CachedFolder folder, string name, ListingTimestamp? acceptListingsAfter)
    {
        var nameHash = HashName(name);
        var lookup = folder.FindName(nameHash, acceptListingsAfter, _listFolder);

        // The lookup has already used its listing. Every folder is listed again on its next use.
        if (_listedNames.Count > _maxListedNames)
        {
            Clear();
        }

        return lookup;
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
                var name = Path.GetFileName(currentPath);
                var nameHash = HashName(name);
                folder.AddName(nameHash);
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

        DropFolders(itemPath);

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
            var name = Path.GetFileName(itemPath);
            var nameHash = HashName(name);
            folder.RemoveName(nameHash);
        }
    }

    /// <summary>
    /// Records that the name of an item at the full path, which is under the backing folder, may have changed. The
    /// watcher reports this for a create, delete or rename, and on macOS the kind it reports can be wrong. The listing
    /// of the folder that contains the item is marked stale, so the folder is listed again on its next use. The folder
    /// is found in any letter case. On macOS the watcher reports a removed item in the letter case of the call that
    /// removed it. Every cached folder at or below the path is dropped too. The watcher's thread calls this, so it
    /// never waits for a listing in progress.
    /// </summary>
    public void RecordNameChanged(string itemPath)
    {
        if (itemPath.Equals(_backingFolderPath, PathComparison.Comparison))
        {
            Clear();
            return;
        }

        DropFolders(itemPath);

        var folderPath = Path.GetDirectoryName(itemPath);
        if (folderPath is null)
        {
            return;
        }

        if (_cachedFolders.TryGetValue(folderPath, out var folders))
        {
            foreach (var folder in folders)
            {
                folder.MarkStale();
            }
        }
    }

    /// <summary>
    /// Drops every cached folder and listing.
    /// </summary>
    public void Clear()
    {
        lock (_writeLock)
        {
            _cachedFolders.Clear();
        }

        Interlocked.Exchange(ref _cachedFolderCount, 0);
        _listedNames.Reset();
    }

    // Reads the names stored in the folder. The cached folder calls this while holding the folder's lock. The change
    // count passed in was read before the listing started.
    private FolderListing? ListFolder(string folderPath, int changeCountAtStart)
    {
        var startedAt = _listingClock.Advance();

        HashSet<ulong> nameHashes;
        try
        {
            var listing = new FileSystemEnumerable<ulong>(
                folderPath,
                (ref System.IO.Enumeration.FileSystemEntry entry) => HashName(entry.FileName),
                ListingOptions);
            nameHashes = new HashSet<ulong>(listing);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        Interlocked.Increment(ref _listingCount);

        return new FolderListing(nameHashes, startedAt, changeCountAtStart);
    }

    // Hashes the name's UTF-16 code units to 64 bits. When two names share a hash, the check cannot tell them
    // apart, and a key that differs from the stored name passes. With a million names cached, about one lookup in
    // 10^13 finds a shared hash.
    private static ulong HashName(ReadOnlySpan<char> name)
    {
        return XxHash3.HashToUInt64(MemoryMarshal.AsBytes(name));
    }

    // Returns the folder stored under exactly the path, or null.
    private static CachedFolder? FindExactPath(CachedFolder[] folders, string folderPath)
    {
        foreach (var folder in folders)
        {
            if (string.Equals(folder.FolderPath, folderPath, StringComparison.Ordinal))
            {
                return folder;
            }
        }

        return null;
    }

    // Drops one folder, and every cached folder below it. Another thread may have dropped the folder already. The
    // folder is then left alone.
    private void RemoveFolder(CachedFolder folder)
    {
        lock (_writeLock)
        {
            if (!_cachedFolders.TryGetValue(folder.FolderPath, out var folders)
                || !folders.Contains(folder))
            {
                return;
            }

            var remainingFolders = folders.Where(cached => cached != folder).ToArray();
            if (remainingFolders.Length == 0)
            {
                _cachedFolders.TryRemove(folder.FolderPath, out _);
            }
            else
            {
                _cachedFolders[folder.FolderPath] = remainingFolders;
            }
        }

        DropRemovedFolder(folder);
    }

    // Drops the cached folders at the path, in any letter case, and every cached folder below them.
    private void DropFolders(string folderPath)
    {
        CachedFolder[]? folders;
        lock (_writeLock)
        {
            _cachedFolders.TryRemove(folderPath, out folders);
        }

        if (folders is null)
        {
            return;
        }

        foreach (var folder in folders)
        {
            DropRemovedFolder(folder);
        }
    }

    // Drops every cached folder below a folder just removed from the cache. Each folder counts its cached children.
    // The scan runs only for a folder with a count above zero.
    private void DropRemovedFolder(CachedFolder folder)
    {
        ForgetFolder(folder);

        folder.Parent?.RemoveCachedChild();

        if (!folder.HasCachedChildren)
        {
            return;
        }

        var descendantPrefix = folder.FolderPath + Path.DirectorySeparatorChar;
        foreach (var entry in _cachedFolders)
        {
            if (!entry.Key.StartsWith(descendantPrefix, PathComparison.Comparison))
            {
                continue;
            }

            CachedFolder[]? descendants;
            lock (_writeLock)
            {
                _cachedFolders.TryRemove(entry.Key, out descendants);
            }

            foreach (var descendant in descendants ?? [])
            {
                ForgetFolder(descendant);
            }
        }
    }

    private void ForgetFolder(CachedFolder folder)
    {
        Interlocked.Decrement(ref _cachedFolderCount);
        folder.Forget();
    }
}
