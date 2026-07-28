namespace Celbridge.Console;

/// <summary>
/// The console service owns the workspace's open-console registry and the process owner that tears down
/// their child processes.
/// </summary>
public interface IConsoleService
{
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
}
