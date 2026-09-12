namespace Celbridge.Utilities;

/// <summary>
/// A compiled set of resource path patterns, evaluated as a whole. A pattern that
/// matches a folder matches everything beneath it, so "bin", "**/bin" and "src/bin"
/// all take their subtree with them.
/// </summary>
public sealed class ResourcePatternSet
{
    private readonly IReadOnlyList<ResourcePathMatcher> _matchers;

    /// <summary>
    /// A set with no patterns, which matches nothing.
    /// </summary>
    public static ResourcePatternSet Empty { get; } = new(Array.Empty<ResourcePathMatcher>());

    private ResourcePatternSet(IReadOnlyList<ResourcePathMatcher> matchers)
    {
        _matchers = matchers;
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

        if (MatchesAny(resourcePath, isFolder))
        {
            return true;
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
            if (MatchesAny(ancestor, isFolder: true))
            {
                return true;
            }

            searchStart = slashIndex + 1;
        }
    }

    private bool MatchesAny(string resourcePath, bool isFolder)
    {
        foreach (var matcher in _matchers)
        {
            if (matcher.IsMatch(resourcePath, isFolder))
            {
                return true;
            }
        }

        return false;
    }
}
