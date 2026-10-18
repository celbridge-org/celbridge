using Celbridge.Packages;

namespace Celbridge.Utilities;

/// <summary>
/// Parses and compares the paths of package folders. Every project has the packages folder. The project file
/// can list more, each as a path from the project root.
/// </summary>
public static class PackageFolderPath
{
    /// <summary>
    /// The packages folder. Every project has one, and a project file never lists it.
    /// </summary>
    public static ResourceKey DefaultFolder { get; } = new ResourceKey(PackageConstants.DefaultPackagesFolder);

    /// <summary>
    /// Parses a listed folder path, ignoring whitespace and slashes at either end. Fails for the project root,
    /// for a path that is not a valid resource path, and for a path under another root.
    /// </summary>
    public static bool TryParse(string path, out ResourceKey folder)
    {
        folder = ResourceKey.Empty;

        if (IsProjectRoot(path))
        {
            return false;
        }

        var trimmedPath = TrimPath(path);
        if (!ResourceKey.TryCreate(trimmedPath, out var resource)
            || resource.IsEmpty
            || resource.Root != ResourceKey.DefaultRoot)
        {
            return false;
        }

        folder = resource;
        return true;
    }

    /// <summary>
    /// Returns true if the path names the project root once whitespace and slashes at either end are ignored.
    /// </summary>
    public static bool IsProjectRoot(string path)
    {
        var trimmedPath = TrimPath(path);

        return trimmedPath.Length == 0
            || trimmedPath == ".";
    }

    /// <summary>
    /// Returns true if the two paths name the same folder. Letter case counts only on a case-sensitive file system.
    /// </summary>
    public static bool IsSameFolder(string folderPath, string otherFolderPath)
    {
        return string.Equals(folderPath, otherFolderPath, PathComparison.Comparison);
    }

    /// <summary>
    /// Returns the folders from the list that can be package folders, in their parsed form. Drops the packages
    /// folder, any folder listed earlier, and any path that fails to parse.
    /// </summary>
    public static IReadOnlyList<string> NormalizeListedFolders(IEnumerable<string> listedFolders)
    {
        var folders = new List<string>();
        foreach (var listedFolder in listedFolders)
        {
            if (!TryParse(listedFolder, out var folder)
                || IsListed(folders, folder.Path))
            {
                continue;
            }

            folders.Add(folder.Path);
        }

        return folders;
    }

    /// <summary>
    /// Returns true if the folder is the packages folder or already in the list.
    /// </summary>
    public static bool IsListed(IReadOnlyList<string> listedFolders, string folderPath)
    {
        if (IsSameFolder(folderPath, DefaultFolder.Path))
        {
            return true;
        }

        return listedFolders.Any(listedFolder => IsSameFolder(listedFolder, folderPath));
    }

    private static string TrimPath(string path)
    {
        return path.Trim().Trim('/');
    }
}
