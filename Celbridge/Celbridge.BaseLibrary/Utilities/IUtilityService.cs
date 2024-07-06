namespace Celbridge.BaseLibrary.Utilities;

/// <summary>
/// Provides access to common low-level utility methods.
/// </summary>
public interface IUtilityService
{
    /// <summary>
    /// Returns true if the string represents a valid resource path.
    /// Resource paths are similar to file system paths but with additional constraints:
    /// - Specified relative to the project folder. 
    /// - Absolute paths, parent and same directory referencs are not supported.
    /// - '/' is used as the path separator on all platforms, backslashes are not allowed.
    bool IsValidResourcePath(string resourcePath);

    /// <summary>
    /// Returns true if the string represents a valid path segment.
    /// </summary>
    bool IsValidPathSegment(string segment);

    /// <summary>
    /// Returns a path to a randomly named file in temporary storage.
    /// The path includes the specified folder name and extension.
    /// </summary>
    string GetTemporaryFilePath(string folderName, string extension);
}
