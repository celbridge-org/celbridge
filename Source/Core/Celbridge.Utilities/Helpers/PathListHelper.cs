using Path = System.IO.Path;

namespace Celbridge.Utilities;

/// <summary>
/// Builds the separator-delimited folder lists used for PATH and its relatives.
/// </summary>
public static class PathListHelper
{
    /// <summary>
    /// Puts a folder at the front of a path list, moving it there when the list already carries it
    /// elsewhere. Fails when the folder contains the platform's path separator, which no entry can
    /// represent.
    /// </summary>
    public static bool TryPrepend(string? pathList, string folder, out string result)
    {
        if (string.IsNullOrEmpty(folder) ||
            folder.Contains(Path.PathSeparator))
        {
            result = pathList ?? string.Empty;
            return false;
        }

        if (string.IsNullOrEmpty(pathList))
        {
            result = folder;
            return true;
        }

        // Any existing entry is dropped rather than left alone, so the caller's folder is reached first
        // whatever the inherited list already held.
        var remaining = pathList
            .Split(Path.PathSeparator)
            .Where(entry => !string.Equals(entry, folder, PathComparison.Comparison))
            .ToList();

        result = remaining.Count == 0
            ? folder
            : folder + Path.PathSeparator + string.Join(Path.PathSeparator, remaining);

        return true;
    }
}
