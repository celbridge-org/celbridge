using Celbridge.Utilities;

namespace Celbridge.LanguageServers.Helpers;

/// <summary>
/// Converts between an absolute file path and a file URI. A language server knows each file by its URI. The drive
/// letter is lowercase, and every character outside the unreserved set is percent-encoded, the drive's colon
/// included. So D:\Docs\a b.ts becomes file:///d%3A/Docs/a%20b.ts.
/// </summary>
public static class LspUri
{
    private const string FileScheme = "file://";

    /// <summary>
    /// Returns the file URI of an absolute path.
    /// </summary>
    public static string FromPath(string path)
    {
        var normalizedPath = path.Replace('\\', '/');

        // A drive path gains a leading slash, and the drive letter becomes lowercase.
        if (HasDriveLetter(normalizedPath, 0))
        {
            normalizedPath = "/" + char.ToLowerInvariant(normalizedPath[0]) + normalizedPath.Substring(1);
        }

        var segments = normalizedPath.Split('/');
        var encodedSegments = segments.Select(Uri.EscapeDataString);
        var encodedPath = string.Join("/", encodedSegments);

        return FileScheme + encodedPath;
    }

    /// <summary>
    /// Returns a file URI's absolute path, or an empty string when the text is not a file URI.
    /// </summary>
    public static string ToPath(string uri)
    {
        if (!uri.StartsWith(FileScheme, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var encodedPath = uri.Substring(FileScheme.Length);
        var path = Uri.UnescapeDataString(encodedPath);

        // A drive path loses the leading slash and takes the platform's separator.
        if (path.StartsWith('/')
            && HasDriveLetter(path, 1))
        {
            path = path.Substring(1).Replace('/', '\\');
        }

        return path;
    }

    /// <summary>
    /// Returns true if both URIs name the same file. Letter case counts only on a file system that tells names
    /// apart by case.
    /// </summary>
    public static bool AreSameFile(string firstUri, string secondUri)
    {
        var firstPath = ToPath(firstUri);
        var secondPath = ToPath(secondUri);
        if (string.IsNullOrEmpty(firstPath)
            || string.IsNullOrEmpty(secondPath))
        {
            return false;
        }

        return string.Equals(firstPath, secondPath, PathComparison.Comparison);
    }

    private static bool HasDriveLetter(string path, int index)
    {
        return path.Length > index + 1
            && char.IsAsciiLetter(path[index])
            && path[index + 1] == ':';
    }
}
