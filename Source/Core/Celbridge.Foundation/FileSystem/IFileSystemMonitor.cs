namespace Celbridge.FileSystem;

/// <summary>
/// Discriminates the kind of change surfaced by an IFileSystemMonitor.
/// </summary>
public enum FileSystemMonitorEventKind
{
    /// <summary>
    /// A file or folder was created.
    /// </summary>
    Created,

    /// <summary>
    /// The content of a file changed. The monitor coalesces the burst of raw
    /// change notifications a single write emits into one settled event.
    /// </summary>
    Changed,

    /// <summary>
    /// A file or folder was deleted.
    /// </summary>
    Deleted,

    /// <summary>
    /// A file or folder was renamed or moved within the watched subtree.
    /// </summary>
    Renamed,
}

/// <summary>
/// A single filesystem change surfaced by an IFileSystemMonitor. Path is the
/// current absolute path of the affected item. OldPath is the previous absolute
/// path for a Renamed event and null for every other kind.
/// </summary>
public record FileSystemMonitorEvent(
    FileSystemMonitorEventKind Kind,
    string Path,
    string? OldPath);

/// <summary>
/// Watches a single backing folder subtree and surfaces clean, path-based
/// created / changed / deleted / renamed events. Implementations own the
/// underlying substrate watcher and coalesce the burst of raw change
/// notifications a single write emits into one settled Changed event. Callers
/// map the raw paths onto higher-level concepts and apply their own filtering.
/// </summary>
public interface IFileSystemMonitor : IDisposable
{
    /// <summary>
    /// Raised when a file or folder within the watched backing folder is
    /// created, changed, deleted, or renamed.
    /// </summary>
    event EventHandler<FileSystemMonitorEvent>? FileSystemChanged;

    /// <summary>
    /// Begins watching the backing folder subtree. Fails if the backing folder
    /// does not exist or the underlying watcher cannot be created.
    /// </summary>
    Result Start();
}
