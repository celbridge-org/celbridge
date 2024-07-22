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

    /// <summary>
    /// Returns environment information the runtime application.
    /// </summary>
    EnvironmentInfo GetEnvironmentInfo();

    /// <summary>
    /// Returns the current UTC time in "yyyyMMdd_HHmmss" format.
    /// </summary>
    public string GetTimestamp();

    /// <summary>
    /// Deletes old files in the specified folder that start with the specified prefix.
    /// </summary>
    Result DeleteOldFiles(string folderPath, string filePrefix, int maxFilesToKeep);
}
