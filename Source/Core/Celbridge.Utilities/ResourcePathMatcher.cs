using System.Text;
using System.Text.RegularExpressions;
using Celbridge.Resources;

namespace Celbridge.Utilities;

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
/// Compiles a glob-style resource path pattern into a reusable matcher. Several
/// patterns are evaluated together through ResourcePatternSet.
/// </summary>
public sealed class ResourcePathMatcher : IPathMatcher
{
    private readonly string _pattern;
    private readonly Regex _regex;
    private readonly PathMatchTarget _target;
    private readonly bool _matchesAtAnyDepth;

    // One regex for each segment of the pattern. A pattern that matches at any depth has none. A null entry is a
    // segment that can match any number of folders.
    private readonly IReadOnlyList<Regex?> _segmentRegexes;

    /// <summary>
    /// The original pattern string as supplied at construction.
    /// </summary>
    public string Pattern => _pattern;

    /// <summary>
    /// Whether this pattern restricts matches to folders.
    /// </summary>
    public PathMatchTarget Target => _target;

    /// <summary>
    /// Returns true if the pattern matches one name at any depth in the path. A bare name
    /// like 'build', compiled in the project's syntax, does. An anchored pattern never does.
    /// </summary>
    public bool MatchesAtAnyDepth => _matchesAtAnyDepth;

    private ResourcePathMatcher(
        string pattern,
        Regex regex,
        PathMatchTarget target,
        bool matchesAtAnyDepth,
        IReadOnlyList<Regex?> segmentRegexes)
    {
        _pattern = pattern;
        _regex = regex;
        _target = target;
        _matchesAtAnyDepth = matchesAtAnyDepth;
        _segmentRegexes = segmentRegexes;
    }

    /// <summary>
    /// Compiles a pattern into a reusable matcher. Use sparingly at hot paths;
    /// the regex compile is cached for the life of the instance. With ignoreCase
    /// set, the pattern matches a path in any letter case. With anchored set, the
    /// pattern is relative to the root, so a pattern without a slash names one item
    /// at the top level.
    /// </summary>
    public static ResourcePathMatcher Compile(string pattern, bool ignoreCase = false, bool anchored = false)
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

        bool matchesAtAnyDepth = !anchored
            && !workingPattern.Contains('/');
        var regex = BuildRegex(workingPattern, ignoreCase);

        IReadOnlyList<Regex?> segmentRegexes = Array.Empty<Regex?>();
        if (!matchesAtAnyDepth)
        {
            segmentRegexes = BuildSegmentRegexes(workingPattern, ignoreCase);
        }

        return new ResourcePathMatcher(pattern, regex, target, matchesAtAnyDepth, segmentRegexes);
    }

    /// <summary>
    /// Returns true if the resource path matches this pattern. The path is the
    /// ResourceKey's Path portion (no root prefix, forward-slash separator,
    /// never a leading or trailing slash). In the project's syntax, a bare name matches
    /// any path with a segment of that name. So 'build' catches the folder named build
    /// and everything under it. An anchored bare name matches one item at the top level.
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
    /// Returns false when no path inside the folder can match the pattern. The folder's path
    /// is the ResourceKey's Path portion, and an empty path names the root. A pattern that
    /// glues '**' to other characters can get a cautious true.
    /// </summary>
    public bool CanMatchInside(string folderPath)
    {
        if (_matchesAtAnyDepth)
        {
            return true;
        }

        // The pattern segments that the next segment of a path may match.
        var positions = new HashSet<int>();
        AddPosition(positions, 0);

        if (!string.IsNullOrEmpty(folderPath))
        {
            foreach (var folderSegment in folderPath.Split('/'))
            {
                var nextPositions = new HashSet<int>();
                foreach (var position in positions)
                {
                    if (position == _segmentRegexes.Count)
                    {
                        continue;
                    }

                    var segmentRegex = _segmentRegexes[position];
                    if (segmentRegex is null)
                    {
                        AddPosition(nextPositions, position);
                    }
                    else if (segmentRegex.IsMatch(folderSegment))
                    {
                        AddPosition(nextPositions, position + 1);
                    }
                }

                if (nextPositions.Count == 0)
                {
                    return false;
                }

                positions = nextPositions;
            }
        }

        // A path inside the folder has at least one more segment, so a pattern segment must be left to match it.
        return positions.Any(position => position < _segmentRegexes.Count);
    }

    // Adds the position. A segment that can match any number of folders can also match none, so the position after
    // each such segment is added too.
    private void AddPosition(HashSet<int> positions, int position)
    {
        while (positions.Add(position)
            && position < _segmentRegexes.Count
            && _segmentRegexes[position] is null)
        {
            position++;
        }
    }

    // Builds a regex for each segment of the pattern, to match one name. A segment that contains '**' gets no regex,
    // and can match any number of folders.
    private static IReadOnlyList<Regex?> BuildSegmentRegexes(string pattern, bool ignoreCase)
    {
        var options = RegexOptions.CultureInvariant;
        if (ignoreCase)
        {
            options |= RegexOptions.IgnoreCase;
        }

        var segmentRegexes = new List<Regex?>();
        foreach (var segment in pattern.Split('/'))
        {
            if (segment.Contains("**"))
            {
                segmentRegexes.Add(null);
                continue;
            }

            var builder = new StringBuilder();
            builder.Append('^');
            foreach (var c in segment)
            {
                if (c == '*')
                {
                    builder.Append("[^/]*");
                }
                else
                {
                    builder.Append(Regex.Escape(c.ToString()));
                }
            }
            builder.Append('$');

            segmentRegexes.Add(new Regex(builder.ToString(), options));
        }

        return segmentRegexes;
    }

    private static Regex BuildRegex(string pattern, bool ignoreCase)
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
        if (ignoreCase)
        {
            options |= RegexOptions.IgnoreCase;
        }

        return new Regex(builder.ToString(), options);
    }
}
