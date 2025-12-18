namespace Celbridge.Console;

/// <summary>
/// Error states that the python console can get into.
/// </summary>
public enum ConsoleErrorType
{
    /// <summary>
    /// The .celbridge project config file doesn't exist or failed to parse.
    /// </summary>
    InvalidProjectConfig,

    /// <summary>
    /// Initialization failed prior to starting the python host process.
    /// </summary>
    PythonHostPreInitError,

    /// <summary>
    /// The python host process exited unexpectedly.
    /// </summary>
    PythonHostProcessError,

    /// <summary>
    /// The current version of Celbridge is incompatible with the project version.
    /// This typically indicates that the project was created with a newer version of Celbridge.
    /// </summary>
    IncompatibleVersion,

    /// <summary>
    /// The project version is invalid or not specified.
    /// </summary>
    InvalidVersion,

    /// <summary>
    /// Project migration failed.
    /// </summary>
    MigrationError,
}

/// <summary>
/// Message sent when the console fails to initialize or terminates unexpectedly.
/// </summary>
public record ConsoleErrorMessage(ConsoleErrorType ErrorType, string ConfigFileName);

/// <summary>
/// Message sent to request reloading the current project.
/// </summary>
public record ReloadProjectMessage();
