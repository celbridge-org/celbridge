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
}
