using System.Security.Cryptography;
using System.Text;
using Celbridge.FileSystem;
using Celbridge.Resources;
using Path = System.IO.Path;

namespace Celbridge.Utilities;

/// <summary>
/// Utility methods for computing SHA256 hashes of files, strings, and folder
/// structures.
/// </summary>
public static class FileHashHelper
{
    /// <summary>
    /// Computes a SHA256 hash of a file's contents by reading the path directly.
    /// Intended for files that live outside the resource system (e.g. the Python
    /// install folder); resource-tracked files should hash via
    /// IResourceFileSystem.ComputeHashAsync so the read goes through the gateway.
    /// Returns empty string if the file doesn't exist or can't be read.
    /// </summary>
    public static string HashFileContents(string filePath)
    {
        try
        {
            var fileSystem = ServiceLocator.AcquireService<IFileSystem>();

            var infoResult = SyncRunner.Run(() => fileSystem.GetInfoAsync(filePath));
            if (infoResult.IsFailure
                || infoResult.Value.Kind != StorageItemKind.File)
            {
                return string.Empty;
            }

            var bytesResult = SyncRunner.Run(() => fileSystem.ReadAllBytesAsync(filePath));
            if (bytesResult.IsFailure)
            {
                return string.Empty;
            }

            return HashBytes(bytesResult.Value);
        }
        catch
        {
            // Non-critical: callers handle empty hash gracefully.
        }

        return string.Empty;
    }

    /// <summary>
    /// Computes a SHA256 hash of a UTF-8 string.
    /// </summary>
    public static string HashString(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// Computes a SHA256 hash of a byte array.
    /// </summary>
    public static string HashBytes(byte[] bytes)
    {
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes);
    }

    /// <summary>
    /// Computes a fingerprint of a folder's structure by walking the tree up to
    /// the specified depth and recording each entry's relative path and file size.
    /// Detects files being added, removed, renamed, or replaced in place without
    /// reading file contents. The depth cap keeps the scan bounded on deep trees
    /// like Python's Lib/site-packages while still surfacing the changes that
    /// matter for install-state validation.
    /// </summary>
    public static string HashFolderStructure(string folderPath, int maxDepth = 3)
    {
        var fileSystem = ServiceLocator.AcquireService<IFileSystem>();

        var rootInfoResult = SyncRunner.Run(() => fileSystem.GetInfoAsync(folderPath));
        if (rootInfoResult.IsFailure
            || rootInfoResult.Value.Kind != StorageItemKind.Folder)
        {
            return string.Empty;
        }

        var entries = new List<string>();
        var stack = new Stack<(string Path, int Depth)>();
        stack.Push((folderPath, 0));

        while (stack.Count > 0)
        {
            var (currentPath, depth) = stack.Pop();
            if (depth >= maxDepth)
            {
                continue;
            }

            var filesResult = SyncRunner.Run(() => fileSystem.EnumerateFilesAsync(currentPath, "*", recursive: false));
            var foldersResult = SyncRunner.Run(() => fileSystem.EnumerateFoldersAsync(currentPath));

            // Best effort: a child we cannot enumerate is treated as
            // contributing nothing to the hash. Same as it being absent.
            if (filesResult.IsFailure
                && foldersResult.IsFailure)
            {
                continue;
            }

            if (foldersResult.IsSuccess)
            {
                foreach (var child in foldersResult.Value)
                {
                    var relativePath = Path.GetRelativePath(folderPath, child);
                    entries.Add($"D|{relativePath}");
                    stack.Push((child, depth + 1));
                }
            }

            if (filesResult.IsSuccess)
            {
                foreach (var child in filesResult.Value)
                {
                    var relativePath = Path.GetRelativePath(folderPath, child);
                    long size = 0;
                    var infoResult = SyncRunner.Run(() => fileSystem.GetInfoAsync(child));
                    if (infoResult.IsSuccess
                        && infoResult.Value.Kind == StorageItemKind.File)
                    {
                        size = infoResult.Value.Size;
                    }
                    entries.Add($"F|{relativePath}|{size}");
                }
            }
        }

        entries.Sort(StringComparer.Ordinal);
        return HashString(string.Join("\n", entries));
    }
}
