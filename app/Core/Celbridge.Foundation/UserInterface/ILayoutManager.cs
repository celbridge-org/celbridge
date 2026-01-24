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
    /// Whether the window is currently in a fullscreen mode.
    /// </summary>
    bool IsFullScreen { get; }

    /// <summary>
    /// Current panel visibility state.
    /// </summary>
    PanelVisibilityFlags PanelVisibility { get; }

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
    /// Requests a layout state transition.
    /// </summary>
    Result RequestTransition(LayoutTransition transition);

    /// <summary>
    /// Sets the visibility of a specific panel.
    /// </summary>
    void SetPanelVisibility(PanelVisibilityFlags panel, bool isVisible);

    /// <summary>
    /// Toggles the visibility of a specific panel.
    /// </summary>
    void TogglePanelVisibility(PanelVisibilityFlags panel);
}
