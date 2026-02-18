namespace Celbridge.Workspace;

/// <summary>
/// Identifies individual panels in the workspace for focus tracking.
/// </summary>
public enum WorkspacePanel
{
    /// <summary>
    /// No panel.
    /// </summary>
    None,

    /// <summary>
    /// The Explorer panel.
    /// </summary>
    Explorer,

    /// <summary>
    /// The Search panel.
    /// </summary>
    Search,

    /// <summary>
    /// The Documents panel.
    /// </summary>
    Documents,

    /// <summary>
    /// The Inspector panel.
    /// </summary>
    Inspector,

    /// <summary>
    /// The Console panel.
    /// </summary>
    Console
}

/// <summary>
/// Flags representing which layout regions should be visible.
/// Multiple panels can share a region.
/// </summary>
[Flags]
public enum LayoutRegion
{
    /// <summary>
    /// No regions visible.
    /// </summary>
    None = 0,

    /// <summary>
    /// Primary region (left sidebar containing Explorer and Search).
    /// </summary>
    Primary = 1 << 0,

    /// <summary>
    /// Secondary region (right sidebar containing Inspector).
    /// </summary>
    Secondary = 1 << 1,

    /// <summary>
    /// Console region (bottom panel).
    /// </summary>
    Console = 1 << 2,

    /// <summary>
    /// All regions are visible.
    /// </summary>
    All = Primary | Secondary | Console
}
