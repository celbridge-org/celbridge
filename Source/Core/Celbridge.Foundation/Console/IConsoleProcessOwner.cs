namespace Celbridge.Console;

/// <summary>
/// Owns the child processes of the workspace's open consoles and tears them down together when the
/// project closes or the application crashes.
/// </summary>
public interface IConsoleProcessOwner
{
    /// <summary>
    /// Tracks a console child process so it is killed on project close, and on Windows also on an app
    /// crash. Ignored for a non-positive id.
    /// </summary>
    void Track(int processId);

    /// <summary>
    /// Stops tracking a console child process, once its pty has cleaned it up, so a reused id is not killed.
    /// </summary>
    void Untrack(int processId);
}
