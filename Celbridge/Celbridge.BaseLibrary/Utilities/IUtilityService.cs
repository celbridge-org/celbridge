namespace Celbridge.BaseLibrary.Utilities;

/// <summary>
/// Provides access to common low-level utility methods.
/// </summary>
public interface IUtilityService
{
    /// <summary>
    /// Returns true if the string represents a valid path segment on the current platform.
    /// </summary>
    bool IsPathSegmentValid(string segment);

    /// <summary>
    /// Returns a path to a randomly named file in temporary storage.
    /// The path includes the specified folder name and extension.
    /// </summary>
    string GetTemporaryFilePath(string folderName, string extension);
}
