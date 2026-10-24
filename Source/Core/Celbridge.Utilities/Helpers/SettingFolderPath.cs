using Celbridge.Resources;

namespace Celbridge.Utilities;

/// <summary>
/// Parses a folder that a project setting names as a path from the project root. The downloads folder and the
/// package folders are named this way.
/// </summary>
public static class SettingFolderPath
{
    /// <summary>
    /// Parses a folder path, ignoring whitespace and slashes at either end. Fails for the project root, for a path
    /// that is not a valid resource path, and for a path under another root.
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
    /// Returns true if the folder is a .git folder at any depth, or a folder inside one. Celbridge reserves these
    /// folders.
    /// </summary>
    public static bool IsReserved(ResourceKey folder)
    {
        return GitFolderRule.Matcher.IsMatch(folder.Path, isFolder: true);
    }

    private static string TrimPath(string path)
    {
        return path.Trim().Trim('/');
    }
}
