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
    /// Generate a log file name that starts with the specified prefix.
    /// The name includes timestamp, version and environment.
    /// </summary>
    public string GenerateLogName(string logFilePrefix);

    /// <summary>
    /// Returns the application version.
    /// </summary>
    public string GetAppVersion();

    /// <summary>
    /// Deletes old files in the specified folder that start with the specified prefix.
    /// </summary>
    public Result DeleteOldFiles(string folderPath, string filePrefix, int maxFilesToKeep);
}
