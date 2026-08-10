namespace Celbridge.Workspace;

/// <summary>
/// Manages workspace layout region visibility.
/// </summary>
public interface ILayoutService
{
    /// <summary>
    /// Current region visibility state.
    /// </summary>
    LayoutRegion RegionVisibility { get; }

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
    /// Sets the visibility of a specific region.
    /// </summary>
    void SetRegionVisibility(LayoutRegion region, bool isVisible);

    /// <summary>
    /// Toggles the visibility of a specific region.
    /// </summary>
    void ToggleRegionVisibility(LayoutRegion region);
}
