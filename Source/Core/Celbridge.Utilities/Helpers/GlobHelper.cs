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
    /// Patterns without a path separator match at any depth — "*.py" behaves as
    /// "**/*.py", matching git-ignore semantics. Explicit paths like "src/*.cs"
    /// stay anchored to their declared position.
    /// Examples: "*.py", "src/*.cs", "**/Commands/*.cs", "Services/**/I*.cs"
    /// </summary>
    public static string PathGlobToRegex(string glob)
    {
        if (!glob.Contains('/'))
        {
            glob = "**/" + glob;
        }

        var escaped = Regex.Escape(glob);
        var result = escaped
            .Replace("\\*\\*/", "(?:[^/]*/)*")
            .Replace("\\*\\*", ".*")
            .Replace("\\*", "[^\\\\/]*")
            .Replace("\\?", "[^\\\\/]");
        return $"^{result}$";
    }

    /// <summary>
    /// Builds a case-insensitive regex that matches a single file or folder name
    /// against a comma-separated list of name globs (e.g. "*.js,*.css"), or null
    /// when the list is empty. Each glob is converted via GlobToRegex and the
    /// results are combined as alternatives.
    /// </summary>
    public static Regex? BuildNameMatcher(string globList)
    {
        if (string.IsNullOrEmpty(globList))
        {
            return null;
        }

        var patterns = globList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (patterns.Length == 0)
        {
            return null;
        }

        var alternatives = new List<string>();
        foreach (var pattern in patterns)
        {
            var regexPattern = GlobToRegex(pattern);
            alternatives.Add($"(?:{regexPattern})");
        }

        var combined = string.Join("|", alternatives);
        return new Regex(combined, RegexOptions.IgnoreCase);
    }
}
