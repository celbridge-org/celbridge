using Celbridge.Packages;

namespace Celbridge.Utilities;

/// <summary>
/// The problem, if any, that makes loading the project drop a listed package folder.
/// </summary>
public enum PackageFolderProblem
{
    None,
    ProjectRoot,
    InvalidPath,
    Reserved,
    AlreadyListed,
}

/// <summary>
/// Checks and compares the paths of package folders. Every project has the packages folder. The project file
/// can list more, each as a path from the project root.
/// </summary>
public static class PackageFolderPath
{
    /// <summary>
    /// The packages folder. Every project has one, and a project file never lists it.
    /// </summary>
    public static ResourceKey DefaultFolder { get; } = new ResourceKey(PackageConstants.DefaultPackagesFolder);

    /// <summary>
    /// Returns the problem that makes loading the project drop a listed folder path, or None when the folder can
    /// be a package folder. The packages folder and a folder already in the list count as already listed. On None,
    /// folder is the parsed folder.
    /// </summary>
    public static PackageFolderProblem FindProblem(string path, IReadOnlyList<string> listedFolders, out ResourceKey folder)
    {
        folder = ResourceKey.Empty;

        if (SettingFolderPath.IsProjectRoot(path))
        {
            return PackageFolderProblem.ProjectRoot;
        }

        if (!SettingFolderPath.TryParse(path, out var parsedFolder))
        {
            return PackageFolderProblem.InvalidPath;
        }

        if (SettingFolderPath.IsReserved(parsedFolder))
        {
            return PackageFolderProblem.Reserved;
        }

        if (IsListed(listedFolders, parsedFolder.Path))
        {
            return PackageFolderProblem.AlreadyListed;
        }

        folder = parsedFolder;
        return PackageFolderProblem.None;
    }

    /// <summary>
    /// Returns true if the two paths name the same folder. Letter case counts only on a case-sensitive file system.
    /// </summary>
    public static bool IsSameFolder(string folderPath, string otherFolderPath)
    {
        return string.Equals(folderPath, otherFolderPath, PathComparison.Comparison);
    }

    /// <summary>
    /// Returns the folders from the list that can be package folders, in their parsed form. Drops each path that
    /// FindProblem reports a problem for.
    /// </summary>
    public static IReadOnlyList<string> NormalizeListedFolders(IEnumerable<string> listedFolders)
    {
        var folders = new List<string>();
        foreach (var listedFolder in listedFolders)
        {
            if (FindProblem(listedFolder, folders, out var folder) == PackageFolderProblem.None)
            {
                folders.Add(folder.Path);
            }
        }

        return folders;
    }

    private static bool IsListed(IReadOnlyList<string> listedFolders, string folderPath)
    {
        if (IsSameFolder(folderPath, DefaultFolder.Path))
        {
            return true;
        }

        return listedFolders.Any(listedFolder => IsSameFolder(listedFolder, folderPath));
    }
}
