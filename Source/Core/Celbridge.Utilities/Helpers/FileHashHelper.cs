using System.Security.Cryptography;
using System.Text;
using Celbridge.FileSystem;

namespace Celbridge.Utilities;

/// <summary>
/// Utility methods for computing SHA256 hashes of files, strings, and byte arrays.
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
    public static async Task<string> HashFileContentsAsync(string filePath)
    {
        try
        {
            var fileSystem = ServiceLocator.AcquireService<ILocalFileSystem>();

            var infoResult = await fileSystem.GetInfoAsync(filePath);
            if (infoResult.IsFailure
                || infoResult.Value.Kind != StorageItemKind.File)
            {
                return string.Empty;
            }

            var bytesResult = await fileSystem.ReadAllBytesAsync(filePath);
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
}
