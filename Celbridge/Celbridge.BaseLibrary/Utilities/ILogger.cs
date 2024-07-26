namespace Celbridge.Utilities;

/// <summary>
/// A log file utility, with automatic log file naming and rotation
/// </summary>
public interface ILogger
{
    /// <summary>
    /// Initialize the logger utility.
    /// A log file with the specified logFilePrefix is created in the logFolderPath.
    /// A timestamp is appended to the filename to generate a unique log file.
    /// Previous log files with the same prefix are automatically deleted, but you
    /// can keep a fixed number of old logs via maxFilesToKeep.
    /// </summary>
    Result Initialize(string logFolderPath, string logFilePrefix, int maxFilesToKeep);

    /// <summary>
    /// Write a line of text to the log.
    /// This should be a single line of valid Json text.
    /// </summary>
    Result WriteLine(string line);
}
