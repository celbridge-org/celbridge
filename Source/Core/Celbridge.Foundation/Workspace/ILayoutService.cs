namespace Celbridge.Workspace;

/// <summary>
/// Manages workspace layout surface visibility.
/// </summary>
public interface ILayoutService
{
    /// <summary>
    /// Current surface visibility state.
    /// </summary>
    WorkspaceSurface SurfaceVisibility { get; }

    /// <summary>
    /// Whether the Utility Panel (left sidebar) is currently visible.
    /// </summary>
    bool IsUtilityPanelVisible { get; }

    /// <summary>
    /// Whether the Bottom document area is currently visible.
    /// </summary>
    bool IsBottomAreaVisible { get; }

    /// <summary>
    /// Whether the Side document area is currently visible.
    /// </summary>
    bool IsSideAreaVisible { get; }

    /// <summary>
    /// How far the Bottom document area spans across the workspace.
    /// </summary>
    BottomAreaAlignment BottomAreaAlignment { get; }

    /// <summary>
    /// Sets the visibility of a specific surface.
    /// </summary>
    void SetSurfaceVisibility(WorkspaceSurface surface, bool isVisible);

    /// <summary>
    /// Sets how far the Bottom document area spans across the workspace.
    /// </summary>
    void SetBottomAreaAlignment(BottomAreaAlignment alignment);

    /// <summary>
    /// Toggles the visibility of a specific surface.
    /// </summary>
    void ToggleSurfaceVisibility(WorkspaceSurface surface);
}
