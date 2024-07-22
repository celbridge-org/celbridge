namespace Celbridge.Commands;

/// <summary>
/// Records each command execution in a log file.
/// </summary>
public interface ICommandLogger
{
    /// <summary>
    /// Starts logging command executions.
    /// </summary>
    /// <returns></returns>
    Result StartLogging();
}
