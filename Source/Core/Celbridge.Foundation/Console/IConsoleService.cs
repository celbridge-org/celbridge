namespace Celbridge.Console;

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
    /// Returns the registry of open consoles, which owns the shared cel-proxy JSON-RPC listener and
    /// resolves the Explorer Run menu's targets.
    /// </summary>
    IConsoleSessionRegistry SessionRegistry { get; }

    /// <summary>
    /// Returns the owner of the open consoles' child processes, which tears them down on project close or
    /// app crash.
    /// </summary>
    IConsoleProcessOwner ProcessOwner { get; }

    /// <summary>
    /// Initialize the terminal by spawning a new process.
    /// </summary>
    Task<Result> InitializeTerminalWindow();

    /// <summary>
    /// Runs a command by injecting terminal input.
    /// </summary>
    void RunCommand(string command);
}
