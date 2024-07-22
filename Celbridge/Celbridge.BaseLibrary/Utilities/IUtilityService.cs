namespace Celbridge.Utilities;

/// <summary>
/// Provides access to common low-level utility methods.
/// </summary>
public interface IUtilityService
{
    /// <summary>
    /// Returns a path to a randomly named file in temporary storage.
    /// The path includes the specified folder name and extension.
    /// </summary>
    string GetTemporaryFilePath(string folderName, string extension);
}
