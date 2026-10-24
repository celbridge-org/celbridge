namespace Celbridge.Explorer.Services;

/// <summary>
/// Matches a set of followed folders, and the items directly inside them. Letter case is ignored, so a change
/// reported in another case still matches.
/// </summary>
internal sealed class FollowedFoldersMatcher : IPathMatcher
{
    private readonly HashSet<string> _followedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pathsAboveFollowed = new(StringComparer.OrdinalIgnoreCase);

    public FollowedFoldersMatcher(IEnumerable<ResourceKey> followedFolders)
    {
        foreach (var followedFolder in followedFolders)
        {
            _followedPaths.Add(followedFolder.Path);

            var parentFolder = followedFolder;
            while (!parentFolder.IsEmpty)
            {
                parentFolder = parentFolder.GetParent();
                _pathsAboveFollowed.Add(parentFolder.Path);
            }
        }
    }

    public bool IsMatch(string path, bool isFolder)
    {
        if (_followedPaths.Contains(path))
        {
            return true;
        }

        var parentPath = GetParentPath(path);

        return _followedPaths.Contains(parentPath);
    }

    public bool CanMatchInside(string folderPath)
    {
        return _followedPaths.Contains(folderPath)
            || _pathsAboveFollowed.Contains(folderPath);
    }

    private static string GetParentPath(string path)
    {
        var slashIndex = path.LastIndexOf('/');
        if (slashIndex < 0)
        {
            return string.Empty;
        }

        return path.Substring(0, slashIndex);
    }
}
