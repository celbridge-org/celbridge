namespace Celbridge.Console;

/// <summary>
/// Interface for interacting with the ConsolePanel view.
/// </summary>
public interface IConsolePanel
{
    /// <summary>
    /// Initialize the terminal window displayed in the console panel.
    /// </summary>
    Task<Result> InitializeTerminalWindow(ITerminal terminal);

    /// <summary>
    /// Execute a command using the console script context.
    /// </summary>
    Task<Result> ExecuteCommand(string command, bool logCommand);
}
