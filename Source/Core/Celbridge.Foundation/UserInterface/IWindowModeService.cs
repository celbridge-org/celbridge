namespace Celbridge.UserInterface;

/// <summary>
/// Manages the window's layout mode and full screen state. The layout mode sets which chrome is visible, and
/// full screen sets whether the window fills the screen. Presentation also turns full screen on.
/// </summary>
public interface IWindowModeService
{
    /// <summary>
    /// The current layout mode (chrome level).
    /// </summary>
    LayoutMode LayoutMode { get; }

    /// <summary>
    /// Whether the window is currently filling the screen. On macOS this reflects native fullscreen.
    /// On platforms without native window-chrome controls it reflects the app-driven fullscreen state.
    /// </summary>
    bool IsFullScreen { get; }

    /// <summary>
    /// Whether the window is maximized. It comes from the window's presenter, not from the saved settings.
    /// </summary>
    bool IsMaximized { get; }

    /// <summary>
    /// Requests a layout transition (a layout-mode change or the fullscreen toggle).
    /// </summary>
    Result RequestLayoutTransition(LayoutTransition transition);
}
