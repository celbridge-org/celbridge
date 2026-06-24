namespace Celbridge.DataTransfer;

/// <summary>
/// A file or folder to place on the system clipboard, identified by its full path. IsFolder lets a
/// platform that distinguishes the two (the WinRT storage-item API) pick the right item kind; platforms
/// that address files and folders uniformly (file URLs) can ignore it.
/// </summary>
public record ClipboardFile(string Path, bool IsFolder);

/// <summary>
/// The files and folders read back from the system clipboard, with the transfer mode they were placed
/// under.
/// </summary>
public record ClipboardFiles(IReadOnlyList<string> Paths, DataTransferMode TransferMode);

/// <summary>
/// The system clipboard for files and folders. Abstracts the platform clipboard so the resource
/// copy/paste flow does not depend on one implementation: Windows uses the WinRT data-transfer
/// clipboard, while macOS writes file URLs to NSPasteboard directly because the WinRT storage-item
/// clipboard does not round-trip on the Skia head.
/// </summary>
public interface IFileClipboard
{
    /// <summary>
    /// Places the given files and folders on the system clipboard for a subsequent copy or move.
    /// </summary>
    Task<Result> SetFilesAsync(IReadOnlyList<ClipboardFile> files, DataTransferMode transferMode);

    /// <summary>
    /// The transfer mode of the file content currently on the clipboard, or null when it holds no
    /// files. A cheap synchronous check that does not read the paths.
    /// </summary>
    DataTransferMode? GetFileTransferMode();

    /// <summary>
    /// The files and folders currently on the system clipboard along with their transfer mode, or null
    /// when it holds no files.
    /// </summary>
    Task<ClipboardFiles?> GetFilesAsync();
}
