namespace Celbridge.BaseLibrary.Utilities;

/// <summary>
/// Provides access to common low-level utility methods.
/// </summary>
public interface IUtilityService
{
    /// <summary>
    /// Returns true if the string represents a valid resource key.
    /// Resource keys look similar to regular file paths but with additional constraints:
    /// - Specified relative to the project folder. 
    /// - Absolute paths, parent and same directory references are not supported.
    /// - '/' is used as the path separator on all platforms, backslashes are not allowed.
    bool IsValidResourceKey(string resourceKey);

    /// <summary>
    /// Returns true if the string represents a valid resource key segment.
    /// </summary>
    bool IsValidResourceKeySegment(string resourceKeySegment);

    /// <summary>
    /// Returns a path to a randomly named file in temporary storage.
    /// The path includes the specified folder name and extension.
    /// </summary>
    string GetTemporaryFilePath(string folderName, string extension);
}
