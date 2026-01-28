namespace Celbridge.Resources.Services;

/// <summary>
/// Helper class for common file system operations used by file operations.
/// </summary>
internal static class FileSystemHelper
{
    /// <summary>
    /// Moves a file to a destination, creating the destination directory if needed.
    /// </summary>
    public static void MoveFileWithDirectoryCreation(string sourcePath, string destPath)
    {
        var destDir = Path.GetDirectoryName(destPath)!;
        Directory.CreateDirectory(destDir);
        File.Move(sourcePath, destPath);
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
    /// </summary>
    public static void MoveDirectoryWithParentCreation(string sourcePath, string destPath)
    {
        var destParentDir = Path.GetDirectoryName(destPath)!;
        Directory.CreateDirectory(destParentDir);
        Directory.Move(sourcePath, destPath);
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
