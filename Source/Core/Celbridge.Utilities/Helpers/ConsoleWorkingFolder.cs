using Path = System.IO.Path;

namespace Celbridge.Utilities;

/// <summary>
/// Resolves a console session's working folder: a relative path resolves against the project root, an
/// absolute path is used as given, and a blank value defaults to the project root.
/// </summary>
public static class ConsoleWorkingFolder
{
    public static string Resolve(string workingFolder, string projectFolderPath)
    {
        if (string.IsNullOrWhiteSpace(workingFolder))
        {
            return projectFolderPath;
        }

        if (Path.IsPathRooted(workingFolder))
        {
            return workingFolder;
        }

        var combined = Path.Combine(projectFolderPath, workingFolder);
        return Path.GetFullPath(combined);
    }
}
