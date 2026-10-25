using Celbridge.Resources;

namespace Celbridge.Patterns;

/// <summary>
/// Matches paths against a list of gitignore lines, with git's rules. A line without a slash matches at any depth,
/// a matched folder takes everything inside it, and the last matching line decides.
/// </summary>
public sealed class GitignoreMatcher : IPathMatcher
{
    private readonly IReadOnlyList<GitignoreRule> _rules;

    private GitignoreMatcher(IReadOnlyList<GitignoreRule> rules)
    {
        _rules = rules;
    }

    /// <summary>
    /// Compiles the lines into a matcher. Blank lines and comments match nothing. Fails on the first refused line,
    /// with a message that says what to write instead. A line is refused when git's reading of the line differs from
    /// what the writer meant.
    /// </summary>
    public static Result<GitignoreMatcher> Compile(IReadOnlyList<string> lines, bool ignoreCase)
    {
        var rules = new List<GitignoreRule>();
        foreach (var line in lines)
        {
            var pattern = GitignoreRule.ReadPattern(line);
            if (pattern.Length == 0)
            {
                continue;
            }

            var compileResult = GitignoreRule.Compile(pattern, ignoreCase);
            if (compileResult.IsFailure)
            {
                return Result.Fail(compileResult.FirstErrorMessage);
            }

            var rule = compileResult.Value;
            rules.Add(rule);
        }

        return new GitignoreMatcher(rules);
    }

    public bool IsMatch(string path, bool isFolder)
    {
        if (_rules.Count == 0
            || string.IsNullOrEmpty(path))
        {
            return false;
        }

        // A matched folder takes everything inside it, so each folder on the path is checked first. A line that
        // starts with '!' cannot put back a path inside a matched folder.
        var nameStart = 0;
        var slashIndex = path.IndexOf('/');
        while (slashIndex >= 0)
        {
            var folderPath = path.AsSpan(0, slashIndex);
            var folderName = path.AsSpan(nameStart, slashIndex - nameStart);
            if (IsMatchedByLastRule(folderPath, folderName, isFolder: true))
            {
                return true;
            }

            nameStart = slashIndex + 1;
            slashIndex = path.IndexOf('/', nameStart);
        }

        var name = path.AsSpan(nameStart);

        return IsMatchedByLastRule(path, name, isFolder);
    }

    public bool CanMatchInside(string folderPath)
    {
        if (IsMatch(folderPath, isFolder: true))
        {
            return true;
        }

        // A line that starts with '!' only puts paths back, so only the other lines can match inside the folder.
        foreach (var rule in _rules)
        {
            if (!rule.IsNegative
                && rule.CanMatchInside(folderPath))
            {
                return true;
            }
        }

        return false;
    }

    // The last line that matches the path decides, and a line that starts with '!' decides against a match.
    private bool IsMatchedByLastRule(ReadOnlySpan<char> path, ReadOnlySpan<char> name, bool isFolder)
    {
        for (var index = _rules.Count - 1; index >= 0; index--)
        {
            var rule = _rules[index];
            if (rule.Matches(path, name, isFolder))
            {
                return !rule.IsNegative;
            }
        }

        return false;
    }
}
