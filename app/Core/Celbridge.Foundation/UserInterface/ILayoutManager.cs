namespace Celbridge.UserInterface;

/// <summary>
/// Centralized manager for window modes and panel visibility state.
/// Implements a state machine with clear transitions between allowed states.
/// </summary>
public interface ILayoutManager
{
    /// <summary>
    /// Current window mode.
    /// </summary>
    WindowMode WindowMode { get; }

    /// <summary>
    /// Requests a wondow mode transition.
    /// </summary>
    Result RequestWindowModeTransition(WindowModeTransition transition);

    /// <summary>
    /// Whether the window is currently in a fullscreen mode.
    /// </summary>
    bool IsFullScreen { get; }

    /// <summary>
    /// Current panel visibility state.
    /// </summary>
    PanelRegion PanelVisibility { get; }

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
    void SetPanelVisibility(PanelRegion region, bool isVisible);

    /// <summary>
    /// Toggles the visibility of a specific region.
    /// </summary>
    void TogglePanelVisibility(PanelRegion region);

    /// <summary>
    /// Whether the Console panel is currently maximized to fill the Documents area.
    /// </summary>
    bool IsConsoleMaximized { get; }

    /// <summary>
    /// Sets the maximized state of the Console panel.
    /// When maximized, the Console panel fills the Documents area.
    /// </summary>
    void SetConsoleMaximized(bool isMaximized);
}
