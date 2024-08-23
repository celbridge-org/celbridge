namespace Celbridge.Logging;

/// <summary>
/// Generic wrapper for the Microsoft.Extensions.Logging extension methods.
/// The out keyword specifies that the T parameter is covariant.
/// </summary>
public interface ILogger<out T> : ILogger
{}

/// <summary>
/// Wrapper for the Microsoft.Extensions.Logging extension methods.
/// </summary>
public interface ILogger
{
    /// <summary>
    /// Formats and writes a debug log message, including an exception.
    /// </summary>
    void LogDebug(Exception? exception, string? message, params object?[] args);

    /// <summary>
    /// Formats and writes a debug log message.
    /// </summary>
    void LogDebug(string? message, params object?[] args);

    /// <summary>
    /// Formats and writes a trace log message, including an exception.
    /// </summary>
    void LogTrace(Exception? exception, string? message, params object?[] args);

    /// <summary>
    /// Formats and writes a trace log message.
    /// </summary>
    void LogTrace(string? message, params object?[] args);

    /// <summary>
    /// Formats and writes an informational log message, including an exception.
    /// </summary>
    void LogInformation(Exception? exception, string? message, params object?[] args);

    /// <summary>
    /// Formats and writes an informational log message.
    /// </summary>
    void LogInformation(string? message, params object?[] args);

    /// <summary>
    /// Formats and writes a warning log message, including an exception.
    /// </summary>
    void LogWarning(Exception? exception, string? message, params object?[] args);

    /// <summary>
    /// Formats and writes a warning log message.
    /// </summary>
    void LogWarning(string? message, params object?[] args);

    /// <summary>
    /// Formats and writes an error log message, including an exception.
    /// </summary>
    void LogError(Exception? exception, string? message, params object?[] args);

    /// <summary>
    /// Formats and writes an error log message.
    /// </summary>
    void LogError(string? message, params object?[] args);

    /// <summary>
    /// Formats and writes a critical error log message, including an exception.
    /// </summary>
    void LogCritical(Exception? exception, string? message, params object?[] args);

    /// <summary>
    /// Formats and writes a critical error log message.
    /// </summary>
    void LogCritical(string? message, params object?[] args);

    /// <summary>
    /// Formats the message and creates a logging scope.
    /// </summary>
    IDisposable? BeginScope(string messageFormat, params object?[] args);

    /// <summary>
    /// Shuts down the logger.
    /// </summary>
    void Shutdown();
}
