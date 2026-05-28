namespace Celbridge.Resources.Helpers;

/// <summary>
/// Helper class for common file system operations used by file operations.
/// </summary>
internal static class FileSystemHelper
{
    // Retry budget for cross-process sharing-violation races on file/folder
    // moves. After a file is created, the OS, antivirus, search indexer, or
    // shell can briefly hold a read handle on the file, which surfaces as an
    // IOException ("being used by another process") on an immediate File.Move
    // or Directory.Move. Mirrors the read/write retry budgets in
    // ResourceFileSystem; worst-case wait across all attempts is
    // MoveRetryBaseDelayMs * (1 + 2) = 150ms with the values below.
    private const int MaxMoveAttempts = 3;
    private const int MoveRetryBaseDelayMs = 50;

    /// <summary>
    /// Moves a file to a destination, creating the destination directory if needed.
    /// Retries briefly on transient IOException to absorb cross-process sharing
    /// races; non-IO exceptions fall through unchanged.
    /// </summary>
    public static async Task MoveFileWithDirectoryCreationAsync(string sourcePath, string destPath)
    {
        var destDir = Path.GetDirectoryName(destPath)!;
        Directory.CreateDirectory(destDir);
        await MoveWithRetryAsync(() => File.Move(sourcePath, destPath));
    }

    /// <summary>
    /// Copies a file to a destination, creating the destination directory if needed.
    /// </summary>
    public static void CopyFileWithDirectoryCreation(string sourcePath, string destPath)
    {
        var destDir = Path.GetDirectoryName(destPath)!;
        Directory.CreateDirectory(destDir);
        File.Copy(sourcePath, destPath);
    }

    /// <summary>
    /// Moves a directory to a destination, creating the parent directory if needed.
    /// Retries briefly on transient IOException to absorb cross-process sharing
    /// races; non-IO exceptions fall through unchanged.
    /// </summary>
    public static async Task MoveDirectoryWithParentCreationAsync(string sourcePath, string destPath)
    {
        var destParentDir = Path.GetDirectoryName(destPath)!;
        Directory.CreateDirectory(destParentDir);
        await MoveWithRetryAsync(() => Directory.Move(sourcePath, destPath));
    }

    /// <summary>
    /// Invokes a synchronous file/folder move operation with brief retries on
    /// transient IOException. Non-IO exceptions and the last attempt's
    /// IOException propagate unchanged so persistent failures surface
    /// immediately.
    /// </summary>
    public static async Task MoveWithRetryAsync(Action moveOperation)
    {
        for (var attempt = 1; attempt <= MaxMoveAttempts; attempt++)
        {
            try
            {
                moveOperation();
                return;
            }
            catch (IOException) when (attempt < MaxMoveAttempts)
            {
                await Task.Delay(MoveRetryBaseDelayMs * attempt);
            }
        }
    }

    /// <summary>
    /// Deletes a file if it exists.
    /// </summary>
    public static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Removes empty parent directories starting from the given path.
    /// Continues up the directory tree until a non-empty directory is found.
    /// This is a best-effort operation that silently ignores errors.
    /// </summary>
    public static void CleanupEmptyParentDirectories(string startPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(startPath);
            while (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                if (Directory.GetFiles(dir).Length == 0 && Directory.GetDirectories(dir).Length == 0)
                {
                    Directory.Delete(dir);
                    dir = Path.GetDirectoryName(dir);
                }
                else
                {
                    break;
                }
            }
        }
        catch
        {
            // Best effort cleanup - ignore errors
        }
    }

    /// <summary>
    /// Checks if a directory is empty (contains no files or subdirectories).
    /// </summary>
    public static bool IsDirectoryEmpty(string path)
    {
        return Directory.GetFiles(path).Length == 0 && Directory.GetDirectories(path).Length == 0;
    }
}
