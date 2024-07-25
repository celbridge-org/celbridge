namespace Celbridge.Commands;

/// <summary>
/// Records command executions in a log file.
/// </summary>
public interface IExecutedCommandLogger
{
    /// <summary>
    /// Create the log file in the specified folder.
    /// </summary>
    Result Initialize(string logFolderPath, int maxFilesToKeep);
}
