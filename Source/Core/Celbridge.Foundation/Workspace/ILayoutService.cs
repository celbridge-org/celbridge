namespace Celbridge.Workspace;

/// <summary>
/// Tracks which workspace areas are on screen and how far the Bottom area spans. Main is always visible, so
/// only the Utility Panel and the two collapsible document areas can be hidden.
/// </summary>
public interface ILayoutService
{
    /// <summary>
    /// The areas currently on screen, always including Main.
    /// </summary>
    IReadOnlySet<WorkspaceArea> VisibleAreas { get; }

    /// <summary>
    /// How far the Bottom document area spans across the workspace.
    /// </summary>
    BottomAreaAlignment BottomAreaAlignment { get; }

    /// <summary>
    /// Whether the area is currently on screen. Always true for Main.
    /// </summary>
    bool IsAreaVisible(WorkspaceArea area);

    /// <summary>
    /// Shows or hides an area. Fails for Main, which is always visible.
    /// </summary>
    Result SetAreaVisibility(WorkspaceArea area, bool isVisible);

    /// <summary>
    /// Hides the area when it is showing and shows it when it is hidden. Fails for Main.
    /// </summary>
    Result ToggleAreaVisibility(WorkspaceArea area);

    /// <summary>
    /// Sets how far the Bottom document area spans across the workspace.
    /// </summary>
    void SetBottomAreaAlignment(BottomAreaAlignment alignment);
}
