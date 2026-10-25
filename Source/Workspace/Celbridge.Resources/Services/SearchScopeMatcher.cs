using Celbridge.Utilities;

namespace Celbridge.Resources.Services;

/// <summary>
/// Matches the project paths that a set of search exclusions leaves in.
/// </summary>
internal sealed class SearchScopeMatcher : IPathMatcher
{
    private readonly GitignoreMatcher _searchExclude;

    public SearchScopeMatcher(GitignoreMatcher searchExclude)
    {
        _searchExclude = searchExclude;
    }

    public bool IsMatch(string path, bool isFolder)
    {
        return !_searchExclude.IsMatch(path, isFolder);
    }

    // An excluded folder takes everything inside it out, and the exclusions never match the root.
    public bool CanMatchInside(string folderPath)
    {
        return !_searchExclude.IsMatch(folderPath, isFolder: true);
    }
}
