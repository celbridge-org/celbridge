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
    /// Whether the Context panel (left sidebar) is currently visible.
    /// </summary>
    bool IsContextPanelVisible { get; }

    /// <summary>
    /// Whether the Inspector panel (right sidebar) is currently visible.
    /// </summary>
    bool IsInspectorPanelVisible { get; }

    /// <summary>
    /// Whether the Console panel (bottom panel) is currently visible.
    /// </summary>
    bool IsConsolePanelVisible { get; }

    /// <summary>
    /// Sets the visibility of a specific region.
    /// </summary>
    void SetRegionVisibility(LayoutRegion region, bool isVisible);

    /// <summary>
    /// Toggles the visibility of a specific region.
    /// </summary>
    void ToggleRegionVisibility(LayoutRegion region);
}
