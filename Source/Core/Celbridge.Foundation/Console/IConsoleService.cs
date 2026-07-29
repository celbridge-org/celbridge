namespace Celbridge.Console;

/// <summary>
/// The console service owns the workspace's open-console registry and the process owner that tears down
/// their child processes.
/// </summary>
public interface IConsoleService
{
    /// <summary>
    /// Returns the registry of open consoles.
    /// </summary>
    IConsoleSessionRegistry SessionRegistry { get; }

    /// <summary>
    /// Returns the owner of the open consoles' child processes.
    /// </summary>
    IConsoleProcessOwner ProcessOwner { get; }
}
