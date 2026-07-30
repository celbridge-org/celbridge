namespace Celbridge.Console;

/// <summary>
/// The console service owns the workspace's console sessions and the process owner that tears down their
/// child processes.
/// </summary>
public interface IConsoleService
{
    /// <summary>
    /// Returns the service that runs the workspace's console sessions.
    /// </summary>
    IConsoleSessionService Sessions { get; }

    /// <summary>
    /// Returns the owner of the open consoles' child processes.
    /// </summary>
    IConsoleProcessOwner ProcessOwner { get; }
}
