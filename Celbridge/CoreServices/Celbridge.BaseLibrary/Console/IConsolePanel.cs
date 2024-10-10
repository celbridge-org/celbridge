namespace Celbridge.Console;

/// <summary>
/// Interface for interacting with the ConsolePanel view.
/// </summary>
public interface IConsolePanel
{
    /// <summary>
    /// Initialize the scripting support for the console panel.
    /// </summary>
    Task<Result> InitializeScripting();

    /// <summary>
    /// Execute a command using the console script context.
    /// </summary>
    Task<Result> ExecuteCommand(string command, bool logCommand);
}
