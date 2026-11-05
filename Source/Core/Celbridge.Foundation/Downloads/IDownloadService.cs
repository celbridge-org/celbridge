namespace Celbridge.Downloads;

/// <summary>
/// How far a download got.
/// </summary>
public enum DownloadStatus
{
    /// <summary>
    /// The transfer is running. The file has not reached its destination yet.
    /// </summary>
    InProgress,

    /// <summary>
    /// The transfer finished and the file was moved to its destination.
    /// </summary>
    Succeeded,

    /// <summary>
    /// The download failed to start, its transfer failed, or its file failed to move to the destination. Nothing was
    /// saved.
    /// </summary>
    Failed,

    /// <summary>
    /// The user or the platform stopped the download before the file was saved. A cancellation is not a failure. No
    /// file was saved.
    /// </summary>
    Canceled
}

/// <summary>
/// A download the service has staged. Id identifies the download when the platform reports the outcome, and
/// StagingPath is the absolute path the platform writes the file to.
/// </summary>
public partial record DownloadTicket(
    long Id,
    string StagingPath);

/// <summary>
/// A download recorded this session. FileName is the name the site suggests until the user chooses where the file
/// goes. FileName then becomes the name the file is saved under, uniquified when a project folder already had that
/// name. Resource is empty until the transfer succeeds, and stays empty for a file saved outside the project.
/// FailureReason is empty unless the download failed. TotalBytes is null until the server says how large the file
/// is.
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

    /// <summary>
    /// The absolute path of a file saved outside the project. A file outside the project has no resource key, so
    /// the path replaces Resource. Empty until the transfer succeeds, and for a file saved in the project.
    /// </summary>
    public string FilePath { get; init; } = string.Empty;
}

/// <summary>
/// The platform side of a download that is running. The service holds one for the life of the transfer,
/// which is what keeps the platform's own handle alive.
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
    /// Stages a download that is about to start, records its row, and asks the user where the file goes. The
    /// transfer runs while the user decides. Fails at once while a dialog is open, since only one dialog shows at a
    /// time. The row then records the failure. The transfer is held for as long as the download runs, so the
    /// platform's handle for it stays alive and the download can be canceled.
    /// </summary>
    Task<Result<DownloadTicket>> BeginAsync(string suggestedFileName, string sourceUrl, IDownloadTransfer transfer);

    /// <summary>
    /// Records a download that failed before it started, with a row that gives the reason. The user is not asked
    /// where the file goes.
    /// </summary>
    void RecordFailure(string suggestedFileName, string sourceUrl, string reason);

    /// <summary>
    /// Records how far a running download has got. Safe to call as often as the platform reports.
    /// </summary>
    void ReportProgress(long downloadId, long bytesReceived, long? totalBytes);

    /// <summary>
    /// Records that the transfer finished. The staged file moves to its destination as soon as the user has
    /// chosen one. The move stays off the undo stack.
    /// </summary>
    Task CompleteAsync(long downloadId);

    /// <summary>
    /// Stops a running download and records it as canceled, deleting anything it staged.
    /// </summary>
    Task CancelAsync(long downloadId);

    /// <summary>
    /// Records the download as failed with the given reason, and deletes anything it staged.
    /// </summary>
    Task FailAsync(long downloadId, string reason);

    /// <summary>
    /// Stops a running download whose surface has gone, records it as failed with the given reason, and
    /// deletes anything it staged. Nothing is listening to the transfer any more, so it is stopped here
    /// rather than left to report itself.
    /// </summary>
    Task AbandonAsync(long downloadId, string reason);

    /// <summary>
    /// Records as canceled a download the platform stopped by itself, which is not a failure, and deletes
    /// anything it staged. The transfer is not asked to stop, since it already has.
    /// </summary>
    Task ReportCanceledAsync(long downloadId);

    /// <summary>
    /// Removes the record of a finished download. A download still running keeps its record, and the file a
    /// finished download saved stays where it is.
    /// </summary>
    void Remove(long downloadId);

    /// <summary>
    /// Removes the records of finished downloads. A download still running keeps its record, and the files
    /// finished downloads saved stay where they are.
    /// </summary>
    void ClearAll();

    /// <summary>
    /// Checks that each file a download saved outside the project still exists, and removes the record of each
    /// file that has gone. Files in the project need no check, since the service follows them as they change.
    /// </summary>
    Task CheckFilesOutsideProjectAsync();
}
