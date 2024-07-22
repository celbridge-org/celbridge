namespace Celbridge.Commands;

/// <summary>
/// Records command executions in a log file.
/// </summary>
public interface ICommandLogger
{
    /// <summary>
    /// Starts logging command executions to a log file.
    /// </summary>
    Result StartLogging(string logFilePath, string logFilePrefix, int maxFilesToKeep);
}
