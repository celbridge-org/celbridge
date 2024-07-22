namespace Celbridge.Logging;

public enum LogMessageType
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// Manages logging information to supported log outputs (e.g. in-app console, terminal output, log file, etc.)
/// </summary>
public interface ILoggingService
{
    /// <summary>
    /// Logs a general purpose information message.
    /// </summary>
    public void Info(string message);

    /// <summary>
    /// Logs a warning message.
    /// </summary>
    public void Warn(string message);

    /// <summary>
    /// Logs an error message.
    /// </summary>
    public void Error(string message);
}
