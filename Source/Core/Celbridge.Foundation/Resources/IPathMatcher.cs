namespace Celbridge.Resources;

/// <summary>
/// Decides which paths in a root match a set of patterns.
/// </summary>
public interface IPathMatcher
{
    /// <summary>
    /// Returns true if the path matches. The path is relative to the root, with '/' between names, the same as the
    /// Path portion of a ResourceKey.
    /// </summary>
    bool IsMatch(string path, bool isFolder);

    /// <summary>
    /// Returns false when no path inside the folder can match. A matcher that cannot rule the folder out returns
    /// true. The folder's path is relative to the root, and an empty path names the root.
    /// </summary>
    bool CanMatchInside(string folderPath);
}
