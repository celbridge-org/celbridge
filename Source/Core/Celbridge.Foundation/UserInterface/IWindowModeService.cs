namespace Celbridge.UserInterface;

/// <summary>
/// Manages window mode state and transitions.
/// </summary>
public interface IWindowModeService
{
    /// <summary>
    /// Current window mode.
    /// </summary>
    WindowMode WindowMode { get; }

    /// <summary>
    /// Requests a window mode transition.
    /// </summary>
    Result RequestWindowModeTransition(WindowModeTransition transition);

    /// <summary>
    /// Whether the window is currently in a fullscreen mode.
    /// </summary>
    bool IsFullScreen { get; }
}
