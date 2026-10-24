namespace Celbridge.Commands;

/// <summary>
/// Specify common actions to take when executing a command.
/// </summary>
[Flags]
public enum CommandFlags
{
    None = 0,

    /// <summary>
    /// Save the workspace state after execution.
    /// </summary>
    SaveWorkspaceState = 1 << 3,

    /// <summary>
    /// Suppress the command service's per-command debug log entry for this command.
    /// Typically set on read-only query commands that tools poll frequently, to avoid flooding the log.
    /// </summary>
    SuppressCommandLog = 1 << 4
}
