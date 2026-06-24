namespace Celbridge.UserInterface;

/// <summary>
/// Manages the window's layout mode and fullscreen state. The two are independent: the layout mode
/// controls which chrome is visible, while fullscreen controls whether the window fills the screen.
/// </summary>
public interface IWindowModeService
{
    /// <summary>
    /// The current layout mode (chrome level).
    /// </summary>
    LayoutMode LayoutMode { get; }

    /// <summary>
    /// Whether the window is currently filling the screen. On macOS this reflects native fullscreen;
    /// on platforms without native window-chrome controls it reflects the app-driven fullscreen state.
    /// </summary>
    bool IsFullScreen { get; }

    /// <summary>
    /// Requests a layout transition (a layout-mode change or the fullscreen toggle).
    /// </summary>
    Result RequestLayoutTransition(LayoutTransition transition);
}
