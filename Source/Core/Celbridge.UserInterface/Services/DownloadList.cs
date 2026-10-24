using Celbridge.Downloads;

namespace Celbridge.UserInterface.Services;

/// <summary>
/// The rows of the downloads list, newest first. Each change publishes a new list, so a reader on another
/// thread can keep the list it read. Every change reports whether the rows changed, and the owner decides
/// what to announce.
/// </summary>
internal sealed class DownloadList
{
    /// <summary>
    /// The most rows the list contains at once. Past the limit, the oldest finished row is dropped to make
    /// room. The row of a running download always stays.
    /// </summary>
    public const int Limit = 50;

    private readonly Lock _lock = new();

    // Newest first, which is the order they are listed in.
    private readonly List<DownloadEntry> _entries = new();
    private IReadOnlyList<DownloadEntry> _downloads = Array.Empty<DownloadEntry>();

    private long _nextDownloadId = 1;

    /// <summary>
    /// The rows, newest first.
    /// </summary>
    public IReadOnlyList<DownloadEntry> Downloads
    {
        get
        {
            lock (_lock)
            {
                return _downloads;
            }
        }
    }

    /// <summary>
    /// Returns the file of each succeeded row, once each.
    /// </summary>
    public IReadOnlyList<ResourceKey> GetSucceededFiles()
    {
        lock (_lock)
        {
            return _entries
                .Where(entry => entry.Status == DownloadStatus.Succeeded)
                .Select(entry => entry.Resource)
                .Distinct()
                .ToList();
        }
    }

    /// <summary>
    /// Adds a row for a download that has started, and returns the row's id.
    /// </summary>
    public long AddRunning(string fileName, string sourceUrl)
    {
        lock (_lock)
        {
            var downloadId = TakeDownloadId();

            var entry = new DownloadEntry(
                downloadId,
                fileName,
                ResourceKey.Empty,
                sourceUrl,
                DownloadStatus.InProgress,
                FailureReason: string.Empty,
                DateTimeOffset.UtcNow);

            AddEntry(entry);

            return downloadId;
        }
    }

    /// <summary>
    /// Adds a row for a download that failed before it started.
    /// </summary>
    public void AddFailed(string fileName, string sourceUrl, string reason)
    {
        lock (_lock)
        {
            var entry = new DownloadEntry(
                TakeDownloadId(),
                fileName,
                ResourceKey.Empty,
                sourceUrl,
                DownloadStatus.Failed,
                reason,
                DateTimeOffset.UtcNow);

            AddEntry(entry);
        }
    }

    /// <summary>
    /// Replaces the row with the id by the update's result. Returns true if the list has the row.
    /// </summary>
    public bool Update(long downloadId, Func<DownloadEntry, DownloadEntry> update)
    {
        lock (_lock)
        {
            var entryIndex = _entries.FindIndex(entry => entry.Id == downloadId);
            if (entryIndex < 0)
            {
                return false;
            }

            _entries[entryIndex] = update(_entries[entryIndex]);

            PublishDownloads();

            return true;
        }
    }

    /// <summary>
    /// Removes the row with the id when its download has finished. Returns true if a row was removed.
    /// </summary>
    public bool RemoveFinished(long downloadId)
    {
        return RemoveAll(entry =>
            entry.Id == downloadId &&
            entry.Status != DownloadStatus.InProgress);
    }

    /// <summary>
    /// Removes the row of every finished download. Returns true if a row was removed.
    /// </summary>
    public bool ClearFinished()
    {
        return RemoveAll(entry => entry.Status != DownloadStatus.InProgress);
    }

    /// <summary>
    /// Removes each succeeded row that names one of the files. Returns true if a row was removed.
    /// </summary>
    public bool RemoveSucceeded(IReadOnlySet<ResourceKey> files)
    {
        return RemoveAll(entry =>
            entry.Status == DownloadStatus.Succeeded &&
            files.Contains(entry.Resource));
    }

    /// <summary>
    /// Removes every row. Returns true if a row was removed.
    /// </summary>
    public bool Clear()
    {
        return RemoveAll(_ => true);
    }

    private bool RemoveAll(Predicate<DownloadEntry> match)
    {
        lock (_lock)
        {
            var removedCount = _entries.RemoveAll(match);
            if (removedCount == 0)
            {
                return false;
            }

            PublishDownloads();

            return true;
        }
    }

    private long TakeDownloadId()
    {
        var downloadId = _nextDownloadId;
        _nextDownloadId++;

        return downloadId;
    }

    // The row of a running download stays, so the user can follow the transfer's progress and stop the
    // transfer. When every row is running, the list grows past the limit.
    private void AddEntry(DownloadEntry entry)
    {
        while (_entries.Count >= Limit)
        {
            var oldestSettledIndex = _entries.FindLastIndex(
                candidate => candidate.Status != DownloadStatus.InProgress);
            if (oldestSettledIndex < 0)
            {
                break;
            }

            _entries.RemoveAt(oldestSettledIndex);
        }

        _entries.Insert(0, entry);

        PublishDownloads();
    }

    // Each change publishes a new list, since a reader on another thread may still hold the previous one.
    private void PublishDownloads()
    {
        var downloads = new List<DownloadEntry>(_entries);

        _downloads = downloads.AsReadOnly();
    }
}
