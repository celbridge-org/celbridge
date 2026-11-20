using Celbridge.Utilities;

namespace Celbridge.LanguageServers.Helpers;

/// <summary>
/// Tells the files inside a project folder from the rest. A language server reads only the project folder. So only
/// a file inside the folder gets a server, and only such a file takes a server's edit.
/// </summary>
internal static class ProjectFolder
{
    /// <summary>
    /// Returns true if the file is inside the project folder, at any depth.
    /// </summary>
    public static bool Contains(string projectFolderPath, string filePath)
    {
        var folderPath = Path.TrimEndingDirectorySeparator(projectFolderPath);
        var folderPrefix = folderPath + Path.DirectorySeparatorChar;

        return filePath.StartsWith(folderPrefix, PathComparison.Comparison);
    }
}
