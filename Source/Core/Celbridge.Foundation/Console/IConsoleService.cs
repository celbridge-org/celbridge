namespace Celbridge.Console;

/// <summary>
/// The type of message to print to the console.
/// </summary>
public enum MessageType
{
    Command,
    Information,
    Warning,
    Error,
}

/// <summary>
/// The console service provides functionality to support the console panel in the workspace UI.
/// </summary>
public interface IConsoleService
{
    /// <summary>
    /// Returns the terminal instance created by the console service during initialization. 
    /// </summary>
    ITerminal Terminal { get; }

    /// <summary>
    /// Initialize the terminal by spawning a new process.
    /// </summary>
    Task<Result> InitializeTerminalWindow();

    /// <summary>
    /// Runs a command by injecting terminal input.
    /// </summary>
    void RunCommand(string command);
}
