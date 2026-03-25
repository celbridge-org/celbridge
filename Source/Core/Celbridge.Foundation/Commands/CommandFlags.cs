namespace Celbridge.Commands;

/// <summary>
/// Specify common actions to take when executing a command.
/// </summary>
[Flags]
public enum CommandFlags
{
    None = 0,

    /// <summary>
    /// Force a synchronous resource registry update after execution.
    /// The command will not complete until the registry is up to date.
    /// </summary>
    UpdateResources = 1 << 1,

    /// <summary>
    /// Refresh the resource tree view after execution without updating the resource registry.
    /// Use this for commands that modify the tree view state (e.g. expand/collapse) but do not
    /// change resources on disk.
    /// </summary>
    RefreshResourceTree = 1 << 2,

    /// <summary>
    /// Save the workspace state after execution.
    /// </summary>
    SaveWorkspaceState = 1 << 3
}
