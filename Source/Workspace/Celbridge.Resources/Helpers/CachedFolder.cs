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
/// The hashes of the names stored in a folder, as one listing read them, with the names Celbridge created or removed
/// since. The listing stays current until a change to the folder's names is recorded after the listing started.
/// </summary>
internal sealed class FolderListing
{
    private readonly HashSet<ulong> _nameHashes;
    private readonly int _changeCountAtStart;

    public FolderListing(HashSet<ulong> nameHashes, ListingTimestamp startedAt, int changeCountAtStart)
    {
        _nameHashes = nameHashes;
        _changeCountAtStart = changeCountAtStart;
        StartedAt = startedAt;
    }

    /// <summary>
    /// Returns the timestamp the listing took when it started.
    /// </summary>
    public ListingTimestamp StartedAt { get; }

    /// <summary>
    /// Returns the number of names in the listing.
    /// </summary>
    public int Count => _nameHashes.Count;

    /// <summary>
    /// Returns true if the folder's change count is still the one read when the listing started.
    /// </summary>
    public bool IsCurrent(int changeCount)
    {
        return changeCount == _changeCountAtStart;
    }

    public bool Contains(ulong nameHash)
    {
        return _nameHashes.Contains(nameHash);
    }

    public bool Add(ulong nameHash)
    {
        return _nameHashes.Add(nameHash);
    }

    public bool Remove(ulong nameHash)
    {
        return _nameHashes.Remove(nameHash);
    }
}

/// <summary>
/// A folder the resolver has checked, with the folder's listing. The folder's lock guards the listing. A lookup keeps
/// the lock while the folder is listed, so lookups that arrive together share one listing. Marking the folder stale
/// is lock-free, so the watcher's thread goes on while a listing is in progress.
/// </summary>
internal sealed class CachedFolder
{
    private readonly Lock _lock = new();
    private readonly ListedNameCounter _listedNames;

    // Written under the lock. Forget reads it without the lock.
    private FolderListing? _listing;

    // Counts the changes recorded to the folder's names.
    private int _changeCount;

    private int _cachedChildCount;

    public CachedFolder(string folderPath, CachedFolder? parent, DateTimeOffset cachedAt, ListedNameCounter listedNames)
    {
        FolderPath = folderPath;
        Parent = parent;
        CachedAt = cachedAt;
        _listedNames = listedNames;
    }

    public string FolderPath { get; }

    public CachedFolder? Parent { get; }

    public DateTimeOffset CachedAt { get; }

    /// <summary>
    /// Returns true if a folder below this one is cached.
    /// </summary>
    public bool HasCachedChildren => Volatile.Read(ref _cachedChildCount) > 0;

    public void AddCachedChild()
    {
        Interlocked.Increment(ref _cachedChildCount);
    }

    public void RemoveCachedChild()
    {
        Interlocked.Decrement(ref _cachedChildCount);
    }

    /// <summary>
    /// Records that the folder's names may have changed, so the listing is stale. The call is lock-free.
    /// </summary>
    public void MarkStale()
    {
        Interlocked.Increment(ref _changeCount);
    }

    /// <summary>
    /// Looks a name up in the folder's listing, and lists the folder with listFolder when the listing cannot answer.
    /// A listing started after acceptListingsAfter answers both ways, even when stale. A current listing answers for a
    /// name it contains. A name missing from a current listing may belong to an item created outside Celbridge after
    /// the listing. The folder is then listed again before the name counts as missing. listFolder receives the
    /// folder's path and change count, and returns null when the folder cannot be listed.
    /// </summary>
    public NameLookup FindName(
        ulong nameHash,
        ListingTimestamp? acceptListingsAfter,
        Func<string, int, FolderListing?> listFolder)
    {
        lock (_lock)
        {
            var listing = _listing;
            if (listing is not null)
            {
                if (acceptListingsAfter is { } timestamp
                    && listing.StartedAt.IsAfter(timestamp))
                {
                    return listing.Contains(nameHash) ? NameLookup.Found : NameLookup.Missing;
                }

                if (listing.IsCurrent(Volatile.Read(ref _changeCount))
                    && listing.Contains(nameHash))
                {
                    return NameLookup.Found;
                }
            }

            // The change count is read before the folder is listed, so a change recorded during the listing leaves the
            // new listing stale.
            var newListing = listFolder(FolderPath, Volatile.Read(ref _changeCount));
            if (newListing is null)
            {
                return NameLookup.Unlisted;
            }

            _listedNames.Add(newListing.Count - (listing?.Count ?? 0));
            Volatile.Write(ref _listing, newListing);

            return newListing.Contains(nameHash) ? NameLookup.Found : NameLookup.Missing;
        }
    }

    /// <summary>
    /// Adds the name of an item Celbridge created to the listing, when the folder has one.
    /// </summary>
    public void AddName(ulong nameHash)
    {
        lock (_lock)
        {
            if (_listing is not null
                && _listing.Add(nameHash))
            {
                _listedNames.Add(1);
            }
        }
    }

    /// <summary>
    /// Removes the name of an item Celbridge removed from the listing, when the folder has one.
    /// </summary>
    public void RemoveName(ulong nameHash)
    {
        lock (_lock)
        {
            if (_listing is not null
                && _listing.Remove(nameHash))
            {
                _listedNames.Add(-1);
            }
        }
    }

    /// <summary>
    /// Takes the listing's names out of the count, once the folder has left the cache. The call is lock-free, since
    /// the watcher's thread drops folders.
    /// </summary>
    public void Forget()
    {
        var listing = Volatile.Read(ref _listing);
        _listedNames.Add(-(listing?.Count ?? 0));
    }
}
