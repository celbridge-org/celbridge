namespace Celbridge.Downloads;

/// <summary>
/// How far a download got.
/// </summary>
public enum DownloadStatus
{
    /// <summary>
    /// The transfer is running. Nothing has reached the project yet.
    /// </summary>
    InProgress,

    /// <summary>
    /// The transfer finished and the file was moved to its destination.
    /// </summary>
    Succeeded,

    /// <summary>
    /// The transfer or the move into the project failed. Nothing was left in the project.
    /// </summary>
    Failed,

    /// <summary>
    /// The transfer was stopped before it finished, which is not a failure. Nothing was left in the project.
    /// </summary>
    Canceled
}

/// <summary>
/// What a caller needs to run a download the service has reserved a destination for: the id it reports the
/// outcome against, the absolute path the platform writes the transfer to, and where the file lands.
/// </summary>
public partial record DownloadTicket(
    long Id,
    string StagingPath,
    ResourceKey Destination);

/// <summary>
/// A download recorded this session. FileName is the name the file is saved under, once a taken name has
/// been made unique. Resource is empty until the transfer succeeds, FailureReason is empty unless it
/// failed, and TotalBytes is null while the server has not said how large the file is.
/// </summary>
public partial record DownloadEntry(
    long Id,
    string FileName,
    ResourceKey Resource,
    string SourceUrl,
    DownloadStatus Status,
    string FailureReason,
    DateTimeOffset StartedAt)
{
    /// <summary>
    /// How much of the file has arrived.
    /// </summary>
    public long BytesReceived { get; init; }

    /// <summary>
    /// How large the file is, or null where the server did not say.
    /// </summary>
    public long? TotalBytes { get; init; }
}

/// <summary>
/// The platform side of a download that is running. The service holds one for the life of the transfer,
/// which is also what keeps the platform's own handle for it alive.
/// </summary>
public interface IDownloadTransfer
{
    /// <summary>
    /// Stops the transfer. The platform may report the outcome afterwards or not at all, so the service
    /// settles the download itself rather than waiting to be told.
    /// </summary>
    void Cancel();
}

/// <summary>
/// Decides where every download goes and records the ones this session made. Safe to call from any thread.
/// </summary>
public interface IDownloadService
{
    /// <summary>
    /// The downloads recorded this session, newest first.
    /// </summary>
    IReadOnlyList<DownloadEntry> Downloads { get; }

    /// <summary>
    /// Reserves a destination and a staging path for a download that is about to start. Fails when the
    /// destination is denied by policy, before any bytes move. The transfer is held for as long as the
    /// download runs, so the platform's handle for it stays alive and the download can be cancelled.
    /// </summary>
    Task<Result<DownloadTicket>> BeginAsync(string suggestedFileName, string sourceUrl, IDownloadTransfer transfer);

    /// <summary>
    /// Records how far a running download has got. Safe to call as often as the platform reports.
    /// </summary>
    void ReportProgress(long downloadId, long bytesReceived, long? totalBytes);

    /// <summary>
    /// Moves the staged file to its reserved destination. The move is not recorded for undo, since a
    /// download is not an edit the user made.
    /// </summary>
    Task CompleteAsync(long downloadId);

    /// <summary>
    /// Stops a running download and records it as canceled, deleting anything it staged.
    /// </summary>
    Task CancelAsync(long downloadId);

    /// <summary>
    /// Records the download as failed with the reason its row states, and deletes anything it staged.
    /// </summary>
    Task FailAsync(long downloadId, string reason);

    /// <summary>
    /// Records a download the platform stopped by itself as canceled, which is not a failure, and deletes
    /// anything it staged. The transfer is not asked to stop, since it already has.
    /// </summary>
    Task ReportCanceledAsync(long downloadId);

    /// <summary>
    /// Removes the record of a finished download. A download still running keeps its record, and the file a
    /// finished download landed on stays in the project.
    /// </summary>
    void Remove(long downloadId);

    /// <summary>
    /// Removes the records of finished downloads. A download still running keeps its record, and the files
    /// finished downloads landed on stay in the project.
    /// </summary>
    void ClearAll();
}
