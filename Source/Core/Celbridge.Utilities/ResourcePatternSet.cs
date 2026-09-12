namespace Celbridge.Utilities;

/// <summary>
/// A compiled set of resource path patterns, evaluated as a whole. A pattern that
/// matches a folder matches everything beneath it, so "bin", "**/bin" and "src/bin"
/// all take their subtree with them.
/// </summary>
public sealed class ResourcePatternSet
{
    private readonly IReadOnlyList<ResourcePathMatcher> _matchers;

    // A single-segment pattern already matches that segment anywhere in the path, so
    // only the patterns naming a path need testing against each ancestor prefix.
    private readonly IReadOnlyList<ResourcePathMatcher> _pathMatchers;

    private ResourcePatternSet(IReadOnlyList<ResourcePathMatcher> matchers)
    {
        _matchers = matchers;
        _pathMatchers = matchers.Where(matcher => !matcher.MatchesAtAnyDepth).ToList();
    }

    /// <summary>
    /// Compiles the patterns into a reusable set. Blank entries are dropped.
    /// </summary>
    public static ResourcePatternSet Compile(IEnumerable<string> patterns)
    {
        var matchers = new List<ResourcePathMatcher>();
        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            matchers.Add(ResourcePathMatcher.Compile(pattern));
        }

        return new ResourcePatternSet(matchers);
    }

    /// <summary>
    /// Returns true if any pattern matches the resource path, or matches a folder
    /// the path sits under. The path is the ResourceKey's Path portion
    /// (forward-slash separator, no leading or trailing slash).
    /// </summary>
    public bool IsMatch(string resourcePath, bool isFolder)
    {
        if (_matchers.Count == 0
            || string.IsNullOrEmpty(resourcePath))
        {
            return false;
        }

        if (MatchesAny(_matchers, resourcePath, isFolder))
        {
            return true;
        }

        if (_pathMatchers.Count == 0)
        {
            return false;
        }

        // A matched folder takes its subtree with it, so each ancestor prefix is
        // tested as a folder in its own right.
        var searchStart = 0;
        while (true)
        {
            var slashIndex = resourcePath.IndexOf('/', searchStart);
            if (slashIndex < 0)
            {
                return false;
            }

            var ancestor = resourcePath.Substring(0, slashIndex);
            if (MatchesAny(_pathMatchers, ancestor, isFolder: true))
            {
                return true;
            }

            searchStart = slashIndex + 1;
        }
    }

    private static bool MatchesAny(IReadOnlyList<ResourcePathMatcher> matchers, string resourcePath, bool isFolder)
    {
        foreach (var matcher in matchers)
        {
            if (matcher.IsMatch(resourcePath, isFolder))
            {
                return true;
            }
        }

        return false;
    }
}
