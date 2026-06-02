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
/// Five-rule pattern matcher for the resource policy engine. Anchoring depends
/// on whether the pattern contains a path separator: a bare name with no slash
/// matches at any depth; a pattern with a slash anchors at the path root. '*'
/// matches a single segment; '**' matches recursively. A trailing '/' restricts
/// to folders.
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

    /// <summary>
    /// Returns true if either this pattern or its trailing-slash-equivalent
    /// matches the supplied literal. Used by the built-in override mechanism:
    /// a user writes the literal pattern from the built-in list to suppress it,
    /// and "bin" or "bin/" must both override the built-in "bin/".
    /// </summary>
    public static bool LiteralEquivalent(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return true;
        }
        var leftTrimmed = left.TrimEnd('/');
        var rightTrimmed = right.TrimEnd('/');
        return string.Equals(leftTrimmed, rightTrimmed, StringComparison.Ordinal);
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
