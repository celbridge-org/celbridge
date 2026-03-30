using System.Security.Cryptography;
using System.Text;

namespace Celbridge.Python.Services;

/// <summary>
/// Utility methods for computing SHA256 hashes of files and strings.
/// Used by PythonInstaller and PythonService to detect when Python
/// assets have changed and need to be reinstalled.
/// </summary>
public static class FileHashHelper
{
    /// <summary>
    /// Computes a SHA256 hash of a file's contents. Returns empty string
    /// if the file doesn't exist or can't be read.
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
    /// Computes a fingerprint of a folder by hashing the relative path, size,
    /// and last-write timestamp of every file. Detects any file being added,
    /// deleted, renamed, or modified without reading file contents.
    /// </summary>
    public static string HashFolderStructure(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            return string.Empty;
        }

        var entries = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories)
            .Select(filePath =>
            {
                var relativePath = Path.GetRelativePath(folderPath, filePath);
                var fileInfo = new FileInfo(filePath);
                return $"{relativePath}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}";
            })
            .OrderBy(entry => entry, StringComparer.Ordinal);

        var combined = string.Join("\n", entries);
        return HashString(combined);
    }
}
