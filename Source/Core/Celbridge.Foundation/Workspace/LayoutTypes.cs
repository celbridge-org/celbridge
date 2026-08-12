namespace Celbridge.Workspace;

/// <summary>
/// Identifies individual panels in the workspace for focus tracking.
/// </summary>
public enum WorkspacePanelId
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
/// Flags representing which of the collapsible workspace surfaces should be visible. The Main document
/// area is always visible and is not a surface.
/// </summary>
[Flags]
public enum WorkspaceSurface
{
    /// <summary>
    /// No surfaces visible.
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
    /// All surfaces are visible.
    /// </summary>
    All = UtilityPanel | BottomArea | SideArea
}

/// <summary>
/// How far the Bottom document area spans across the workspace. The area always sits below the Main area;
/// the alignment decides whether it also runs under the Utility Panel, the Side area, or both, and the
/// panels it runs under stop above it.
/// </summary>
public enum BottomAreaAlignment
{
    /// <summary>
    /// The Bottom area spans the Main area only, leaving the Utility Panel and Side area full height.
    /// </summary>
    Center,

    /// <summary>
    /// The Bottom area spans the Utility Panel and the Main area, leaving the Side area full height.
    /// </summary>
    Left,

    /// <summary>
    /// The Bottom area spans the Main area and the Side area, leaving the Utility Panel full height.
    /// </summary>
    Right,

    /// <summary>
    /// The Bottom area spans the whole workspace, so neither the Utility Panel nor the Side area runs
    /// past it.
    /// </summary>
    Justify
}
