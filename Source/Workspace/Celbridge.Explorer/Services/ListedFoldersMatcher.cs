namespace Celbridge.Explorer.Services;

/// <summary>
/// Matches a set of listed folders, and the items directly inside them. Letter case is ignored, so a change
/// reported in another case still matches.
/// </summary>
internal sealed class ListedFoldersMatcher : IPathMatcher
{
    private readonly HashSet<string> _listedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pathsAboveListed = new(StringComparer.OrdinalIgnoreCase);

    public ListedFoldersMatcher(IEnumerable<ResourceKey> listedFolders)
    {
        foreach (var listedFolder in listedFolders)
        {
            _listedPaths.Add(listedFolder.Path);

            var parentFolder = listedFolder;
            while (!parentFolder.IsEmpty)
            {
                parentFolder = parentFolder.GetParent();
                _pathsAboveListed.Add(parentFolder.Path);
            }
        }
    }

    public bool IsMatch(string path, bool isFolder)
    {
        if (_listedPaths.Contains(path))
        {
            return true;
        }

        var parentPath = GetParentPath(path);

        return _listedPaths.Contains(parentPath);
    }

    public bool CanMatchInside(string folderPath)
    {
        return _listedPaths.Contains(folderPath)
            || _pathsAboveListed.Contains(folderPath);
    }

    /// <summary>
    /// Returns true if the folder path is the path, or a path inside it, ignoring letter case.
    /// </summary>
    public static bool IsAtOrInside(string folderPath, string path)
    {
        if (string.Equals(folderPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (path.Length == 0)
        {
            return true;
        }

        return folderPath.Length > path.Length
            && folderPath[path.Length] == '/'
            && folderPath.StartsWith(path, StringComparison.OrdinalIgnoreCase);
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
