namespace Celbridge.Projects;

/// <summary>
/// Resolves and validates the project data folder: the folder inside .celbridge/ that a project's local
/// data is written under. Several configurations in one project folder each name their own, so their
/// data stays separate.
/// </summary>
public static class ProjectDataFolder
{
    /// <summary>
    /// True when the name can serve as a data folder. It is a user-supplied string that builds
    /// filesystem paths inside a folder the resource layer reserves, so anything that could reach
    /// outside that folder is rejected.
    /// </summary>
    public static bool IsValidFolderName(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return false;
        }

        // Separators are rejected here rather than left to the invalid-character set, which varies by
        // platform: a backslash is a legal file name character on macOS but a separator on Windows.
        if (folderName.Contains('/')
            || folderName.Contains('\\'))
        {
            return false;
        }

        if (folderName == "."
            || folderName == "..")
        {
            return false;
        }

        if (Path.IsPathRooted(folderName))
        {
            return false;
        }

        return ResourceKey.IsValidSegment(folderName);
    }

    /// <summary>
    /// The absolute path to the data folder of the project in the given folder. An unnamed data folder
    /// resolves to .celbridge/ itself, which is where a project that never names one keeps its data.
    /// </summary>
    public static string ResolvePath(string projectFolderPath, string folderName)
    {
        var celbridgeFolderPath = Path.Combine(projectFolderPath, ProjectConstants.CelbridgeFolder);

        if (!IsValidFolderName(folderName))
        {
            return celbridgeFolderPath;
        }

        return Path.Combine(celbridgeFolderPath, folderName);
    }
}
