using System.Security.Cryptography;
using System.Text;
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
    /// IFileStorage.ComputeHashAsync so the read goes through the chokepoint.
    /// Returns empty string if the file doesn't exist or can't be read.
    /// </summary>
    public static string HashFileContents(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                var fileBytes = File.ReadAllBytes(filePath);
                return HashBytes(fileBytes);
            }
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
        if (!Directory.Exists(folderPath))
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

            string[] children;
            try
            {
                children = Directory.GetFileSystemEntries(currentPath);
            }
            catch
            {
                // Best effort: a child we cannot enumerate is treated as
                // contributing nothing to the hash. Same as it being absent.
                continue;
            }

            foreach (var child in children)
            {
                var relativePath = Path.GetRelativePath(folderPath, child);
                if (Directory.Exists(child))
                {
                    entries.Add($"D|{relativePath}");
                    stack.Push((child, depth + 1));
                }
                else
                {
                    long size = 0;
                    try
                    {
                        size = new FileInfo(child).Length;
                    }
                    catch
                    {
                        // Treat unreadable file metadata as size 0; the entry's
                        // presence still contributes to the hash.
                    }
                    entries.Add($"F|{relativePath}|{size}");
                }
            }
        }

        entries.Sort(StringComparer.Ordinal);
        return HashString(string.Join("\n", entries));
    }
}
