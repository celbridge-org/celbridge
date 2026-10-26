using System.Text;
using Celbridge.Resources;

namespace Celbridge.Patterns;

/// <summary>
/// Matches paths against a tool's include and exclude patterns, read as ripgrep's globs. Each pattern is a gitignore
/// line that can also use braces, and commas outside braces separate the patterns. Letter case is ignored.
/// </summary>
public sealed class SearchFilter : IPathMatcher
{
    // Null when the include parameter names no pattern, so every path is included.
    private readonly GitignoreMatcher? _include;
    private readonly GitignoreMatcher _exclude;

    private SearchFilter(GitignoreMatcher? include, GitignoreMatcher exclude)
    {
        _include = include;
        _exclude = exclude;
    }

    /// <summary>
    /// Returns true when the include parameter names a pattern that includes paths.
    /// </summary>
    public bool HasIncludePatterns => _include is not null;

    /// <summary>
    /// Compiles the include and exclude parameters. In the include parameter, a pattern that starts with '!'
    /// excludes. Fails with a message that says what to write instead when a pattern is refused.
    /// </summary>
    public static Result<SearchFilter> Compile(string include, string exclude)
    {
        var includeLines = new List<string>();
        var excludeLines = new List<string>();

        foreach (var pattern in SplitPatterns(include))
        {
            if (pattern.StartsWith('!'))
            {
                var addResult = AddExpandedLines(excludeLines, pattern.Substring(1));
                if (addResult.IsFailure)
                {
                    return Result.Fail(addResult.FirstErrorMessage);
                }
            }
            else
            {
                var addResult = AddExpandedLines(includeLines, pattern);
                if (addResult.IsFailure)
                {
                    return Result.Fail(addResult.FirstErrorMessage);
                }
            }
        }

        foreach (var pattern in SplitPatterns(exclude))
        {
            if (pattern.StartsWith('!'))
            {
                return Result.Fail(
                    $"The exclude pattern '{pattern}' starts with '!'. Remove the '!' to exclude the files, or " +
                    "move the pattern to include.");
            }

            var addResult = AddExpandedLines(excludeLines, pattern);
            if (addResult.IsFailure)
            {
                return Result.Fail(addResult.FirstErrorMessage);
            }
        }

        GitignoreMatcher? includeMatcher = null;
        if (includeLines.Count > 0)
        {
            var includeResult = GitignoreMatcher.Compile(includeLines, ignoreCase: true);
            if (includeResult.IsFailure)
            {
                return Result.Fail(includeResult.FirstErrorMessage);
            }

            includeMatcher = includeResult.Value;
        }

        var excludeResult = GitignoreMatcher.Compile(excludeLines, ignoreCase: true);
        if (excludeResult.IsFailure)
        {
            return Result.Fail(excludeResult.FirstErrorMessage);
        }
        var excludeMatcher = excludeResult.Value;

        return new SearchFilter(includeMatcher, excludeMatcher);
    }

    /// <summary>
    /// Returns true if an include pattern matches the path and no exclude pattern does. The path is relative to the
    /// root of the keys searched, with '/' between names.
    /// </summary>
    public bool IsMatch(string path, bool isFolder)
    {
        if (_include is not null
            && !_include.IsMatch(path, isFolder))
        {
            return false;
        }

        return !_exclude.IsMatch(path, isFolder);
    }

    /// <summary>
    /// Returns false when an exclude pattern matches the folder, or when no include pattern can match inside the
    /// folder. The folder's path is relative to the root of the keys searched, and an empty path names the root.
    /// </summary>
    public bool CanMatchInside(string folderPath)
    {
        if (_exclude.IsMatch(folderPath, isFolder: true))
        {
            return false;
        }

        if (_include is null)
        {
            return true;
        }

        return _include.CanMatchInside(folderPath);
    }

    // Adds the gitignore lines from expanding a pattern's braces. A line that would start with '!' or '#' gets a '\'
    // first, since ripgrep reads those characters literally there.
    private static Result AddExpandedLines(List<string> lines, string pattern)
    {
        var expandResult = BraceExpander.Expand(pattern);
        if (expandResult.IsFailure)
        {
            return Result.Fail(expandResult.FirstErrorMessage);
        }

        foreach (var line in expandResult.Value)
        {
            if (line.StartsWith('!')
                || line.StartsWith('#'))
            {
                lines.Add($"\\{line}");
            }
            else
            {
                lines.Add(line);
            }
        }

        return Result.Ok();
    }

    // Splits a parameter on the commas outside braces and classes, and trims the space around each pattern.
    private static IReadOnlyList<string> SplitPatterns(string parameter)
    {
        var patterns = new List<string>();
        if (string.IsNullOrWhiteSpace(parameter))
        {
            return patterns;
        }

        var pattern = new StringBuilder();
        var braceDepth = 0;
        var index = 0;
        while (index < parameter.Length)
        {
            var character = parameter[index];
            if (character == '\\')
            {
                var escapeLength = Math.Min(2, parameter.Length - index);
                pattern.Append(parameter, index, escapeLength);
                index += escapeLength;
                continue;
            }

            if (character == '[')
            {
                var classEnd = BraceExpander.FindClassEnd(parameter, index);
                pattern.Append(parameter, index, classEnd - index);
                index = classEnd;
                continue;
            }

            if (character == ','
                && braceDepth <= 0)
            {
                AddPattern(patterns, pattern.ToString());
                pattern.Clear();
                index++;
                continue;
            }

            if (character == '{')
            {
                braceDepth++;
            }
            else if (character == '}')
            {
                braceDepth--;
            }

            pattern.Append(character);
            index++;
        }

        AddPattern(patterns, pattern.ToString());

        return patterns;
    }

    private static void AddPattern(List<string> patterns, string pattern)
    {
        var trimmed = pattern.Trim();
        if (trimmed.Length > 0)
        {
            patterns.Add(trimmed);
        }
    }
}
