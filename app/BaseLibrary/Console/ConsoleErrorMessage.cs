namespace Celbridge.Console;

/// <summary>
/// Types of console initialization errors.
/// </summary>
public enum ConsoleErrorType
{
    /// <summary>
    /// Failed to parse .celbridge project config file during initialization.
    /// </summary>
    InvalidProjectConfig,

    /// <summary>
    /// Initialization failed prior to starting the Python process.
    /// </summary>
    PythonPreInitError,

    /// <summary>
    /// The python process exited unexpectedly.
    /// </summary>
    PythonProcessError,
}

/// <summary>
/// Message sent when the console fails to initialize or terminates unexpectedly.
/// </summary>
public record ConsoleErrorMessage(ConsoleErrorType ErrorType, string ConfigFileName);

/// <summary>
/// Message sent to request reloading the current project.
/// </summary>
public record ReloadProjectMessage();
