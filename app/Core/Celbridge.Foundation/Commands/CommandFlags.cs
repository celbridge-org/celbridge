namespace Celbridge.Commands;

/// <summary>
/// Specify common actions to take when executing a command.
/// </summary>
[Flags]
public enum CommandFlags
{
    None = 0,

    /// <summary>
    /// Schedule a resource registry update after execution.
    /// Multiple requests are coalesced for efficiency.
    /// </summary>
    RequestUpdateResources = 1 << 1,

    /// <summary>
    /// Force an immediate synchronous resource registry update after execution.
    /// </summary>
    ForceUpdateResources = 1 << 2,

    /// <summary>
    /// Save the workspace state after execution.
    /// </summary>
    SaveWorkspaceState = 1 << 3
}
