using Path = System.IO.Path;

namespace Celbridge.Utilities;

/// <summary>
/// Builds the separator-delimited folder lists used for PATH and its relatives.
/// </summary>
public static class PathListHelper
{
    /// <summary>
    /// Puts a folder at the front of a path list, leaving it where it is when the list already carries it.
    /// Fails when the folder contains the platform's path separator, which no entry can represent.
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

        var isPresent = pathList
            .Split(Path.PathSeparator)
            .Any(entry => string.Equals(entry, folder, PathComparison.Comparison));

        result = isPresent
            ? pathList
            : folder + Path.PathSeparator + pathList;

        return true;
    }
}
