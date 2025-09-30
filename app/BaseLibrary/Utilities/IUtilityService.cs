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
    /// Returns a path which is guaranteed not to clash with any existing file or folder.
    /// </summary>
    Result<string> GetUniquePath(string path);

    /// <summary>
    /// Returns environment information the runtime application.
    /// </summary>
    EnvironmentInfo GetEnvironmentInfo();

    /// <summary>
    /// Returns the current UTC time in "yyyyMMdd_HHmmss" format.
    /// </summary>
    string GetTimestamp();

    /// <summary>
    /// Deletes old files in the specified folder that start with the specified prefix.
    /// </summary>
    Result DeleteOldFiles(string folderPath, string filePrefix, int maxFilesToKeep);

    /// <summary>
    /// Converts a hex color string to an ARGB tuple.
    /// </summary>
    (byte a, byte r, byte g, byte b) ColorFromHex(string hex);

    /// <summary>
    /// Load the content of an embedded resource from the assembly containing the specified type.
    /// </summary>
    Result<string> LoadEmbeddedResource(Type type, string resourcePath);
}
