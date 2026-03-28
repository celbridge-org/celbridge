using System.Text.RegularExpressions;

namespace Celbridge.Utilities;

/// <summary>
/// Helper methods for converting glob patterns to regular expressions.
/// </summary>
public static class GlobHelper
{
    /// <summary>
    /// Converts a simple name glob pattern to a regular expression that matches
    /// against a single file or folder name (no path separators).
    /// Supports * (any characters) and ? (any single character).
    /// Examples: "*.cs", "readme*", "foo?.txt"
    /// </summary>
    public static string GlobToRegex(string glob)
    {
        var regexPattern = Regex.Escape(glob)
            .Replace("\\*", ".*")
            .Replace("\\?", ".");
        return $"^{regexPattern}$";
    }

    /// <summary>
    /// Converts a path glob pattern to a regular expression that matches
    /// against a full forward-slash-separated resource key.
    /// Supports * (any characters within a path segment), ? (any single character
    /// within a path segment), and ** (any characters including path separators).
    /// Examples: "*.py", "src/*.cs", "**/Commands/*.cs", "Services/**/I*.cs"
    /// </summary>
    public static string PathGlobToRegex(string glob)
    {
        var escaped = Regex.Escape(glob);
        var result = escaped
            .Replace("\\*\\*/", "(?:[^/]*/)*")
            .Replace("\\*\\*", ".*")
            .Replace("\\*", "[^\\\\/]*")
            .Replace("\\?", "[^\\\\/]");
        return $"^{result}$";
    }
}
