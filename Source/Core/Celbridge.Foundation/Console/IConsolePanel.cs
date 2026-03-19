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
    /// Runs a command in the console as if the user typed it.
    /// </summary>
    void RunCommand(string command);

    /// <summary>
    /// Shuts down the console panel and releases resources.
    /// </summary>
    void Shutdown();
}
