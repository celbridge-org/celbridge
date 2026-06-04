using System.Text;
using System.Text.RegularExpressions;

namespace Celbridge.Resources.Services;

/// <summary>
/// Indicates whether a compiled pattern applies to both files and folders, or to folders only.
/// </summary>
public enum PathMatchTarget
{
    /// <summary>
    /// Pattern matches both files and folders. Default when no trailing slash is present.
    /// </summary>
    Any,

    /// <summary>
    /// Pattern restricted to folders only (the pattern was written with a trailing slash).
    /// </summary>
    FoldersOnly,
}

/// <summary>
/// Compiles a glob-style resource path pattern into a reusable matcher for the
/// resource policy engine.
/// </summary>
public sealed class ResourcePathMatcher
{
    private readonly string _pattern;
    private readonly Regex _regex;
    private readonly PathMatchTarget _target;
    private readonly bool _matchesAtAnyDepth;

    /// <summary>
    /// The original pattern string as supplied at construction.
    /// </summary>
    public string Pattern => _pattern;

    /// <summary>
    /// Whether this pattern restricts matches to folders.
    /// </summary>
    public PathMatchTarget Target => _target;

    private ResourcePathMatcher(string pattern, Regex regex, PathMatchTarget target, bool matchesAtAnyDepth)
    {
        _pattern = pattern;
        _regex = regex;
        _target = target;
        _matchesAtAnyDepth = matchesAtAnyDepth;
    }

    /// <summary>
    /// Compiles a pattern into a reusable matcher. Use sparingly at hot paths;
    /// the regex compile is cached for the life of the instance.
    /// </summary>
    public static ResourcePathMatcher Compile(string pattern)
    {
        ArgumentException.ThrowIfNullOrEmpty(pattern);

        var workingPattern = pattern;
        var target = PathMatchTarget.Any;
        if (workingPattern.Length > 1
            && workingPattern.EndsWith('/'))
        {
            target = PathMatchTarget.FoldersOnly;
            workingPattern = workingPattern.TrimEnd('/');
        }

        bool matchesAtAnyDepth = !workingPattern.Contains('/');
        var regex = BuildRegex(workingPattern);

        return new ResourcePathMatcher(pattern, regex, target, matchesAtAnyDepth);
    }

    /// <summary>
    /// Returns true if the resource path matches this pattern. The path is the
    /// ResourceKey's Path portion (no root prefix, forward-slash separator,
    /// never a leading or trailing slash). For bare-name (no-slash) patterns
    /// the pattern matches if any path segment matches, so 'build' catches the
    /// folder named build and everything under it.
    /// </summary>
    public bool IsMatch(string resourcePath, bool isFolder)
    {
        if (string.IsNullOrEmpty(resourcePath))
        {
            return false;
        }

        if (_matchesAtAnyDepth)
        {
            var segments = resourcePath.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                if (!_regex.IsMatch(segments[i]))
                {
                    continue;
                }
                bool isLastSegment = i == segments.Length - 1;
                if (_target == PathMatchTarget.FoldersOnly
                    && isLastSegment
                    && !isFolder)
                {
                    continue;
                }
                return true;
            }
            return false;
        }

        if (_target == PathMatchTarget.FoldersOnly
            && !isFolder)
        {
            return false;
        }
        return _regex.IsMatch(resourcePath);
    }

    private static Regex BuildRegex(string pattern)
    {
        var builder = new StringBuilder();
        builder.Append('^');

        int i = 0;
        while (i < pattern.Length)
        {
            var c = pattern[i];
            if (c == '*')
            {
                bool isDoubleStar = i + 1 < pattern.Length
                    && pattern[i + 1] == '*';
                if (!isDoubleStar)
                {
                    // Single '*' stays within one segment.
                    builder.Append("[^/]*");
                    i++;
                    continue;
                }

                i += 2;
                if (i < pattern.Length
                    && pattern[i] == '/')
                {
                    // '**/' spans zero or more whole segments, so the separator
                    // is part of the match and 'a/**/b' will not match 'a/xb'.
                    builder.Append("(?:.*/)?");
                    i++;
                }
                else if (i == pattern.Length)
                {
                    // Trailing '**' matches everything below this point.
                    builder.Append(".*");
                }
                else
                {
                    // '**' glued to a non-separator is not a whole segment, so it
                    // degrades to a single-segment wildcard.
                    builder.Append("[^/]*");
                }
            }
            else if (c == '/')
            {
                builder.Append('/');
                i++;
            }
            else
            {
                builder.Append(Regex.Escape(c.ToString()));
                i++;
            }
        }

        builder.Append('$');

        var options = RegexOptions.Compiled | RegexOptions.CultureInvariant;
        return new Regex(builder.ToString(), options);
    }
}
