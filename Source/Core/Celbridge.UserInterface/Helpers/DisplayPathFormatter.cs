namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Formats absolute filesystem paths for display in the user interface.
/// </summary>
public static class DisplayPathFormatter
{
    private const string HomeFolderAbbreviation = "~";

    /// <summary>
    /// Returns the path with the current user's home folder replaced by a tilde, or the path unchanged when
    /// it lies outside the home folder.
    /// </summary>
    public static string AbbreviateHomeFolder(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        var separators = new[]
        {
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar
        };

        var homeFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd(separators);
        if (string.IsNullOrEmpty(homeFolderPath))
        {
            return path;
        }

        // Windows and macOS both match paths without regard to case; Linux filesystems are case sensitive.
        var comparison = OperatingSystem.IsLinux()
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        if (!path.StartsWith(homeFolderPath, comparison))
        {
            return path;
        }

        // A sibling folder that merely starts with the same characters is not inside the home folder.
        var relativePath = path.Substring(homeFolderPath.Length);
        if (relativePath.Length > 0 &&
            !separators.Contains(relativePath[0]))
        {
            return path;
        }

        return HomeFolderAbbreviation + relativePath;
    }
}
