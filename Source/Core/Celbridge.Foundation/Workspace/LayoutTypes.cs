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
    /// The Documents panel, covering all three document areas.
    /// </summary>
    Documents,

    /// <summary>
    /// The custom-utility surface in the Utility Panel (Explorer and Search have their own values).
    /// </summary>
    CustomUtility,

    /// <summary>
    /// The Project Settings panel.
    /// </summary>
    ProjectSettings
}

/// <summary>
/// Flags representing which of the collapsible workspace regions should be visible. The Main document
/// area is always visible and is not a region.
/// </summary>
[Flags]
public enum LayoutRegion
{
    /// <summary>
    /// No regions visible.
    /// </summary>
    None = 0,

    /// <summary>
    /// The Utility Panel (left sidebar hosting Explorer, Search and Project Settings).
    /// </summary>
    UtilityPanel = 1 << 0,

    /// <summary>
    /// The Bottom document area.
    /// </summary>
    BottomArea = 1 << 1,

    /// <summary>
    /// The Side document area.
    /// </summary>
    SideArea = 1 << 2,

    /// <summary>
    /// All regions are visible.
    /// </summary>
    All = UtilityPanel | BottomArea | SideArea
}
