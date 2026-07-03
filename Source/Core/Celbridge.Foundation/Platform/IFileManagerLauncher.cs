namespace Celbridge.Platform;

/// <summary>
/// Opens files and folders through the operating system's shell: launching a file in its associated
/// application and revealing a file or folder in the native file manager.
/// </summary>
public interface IFileManagerLauncher
{
    /// <summary>
    /// Opens the file at the given path in its associated application, or reveals the path in the file
    /// manager when it is not a file.
    /// </summary>
    Task<Result> OpenApplicationAsync(string path);

    /// <summary>
    /// Reveals the file or folder at the given path in the native file manager, selecting the item when it
    /// is a file.
    /// </summary>
    Task<Result> OpenFileManagerAsync(string path);
}
