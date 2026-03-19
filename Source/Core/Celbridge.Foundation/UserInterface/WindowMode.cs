namespace Celbridge.UserInterface;

/// <summary>
/// Defines the window modes for the application.
/// </summary>
public enum WindowMode
{
    /// <summary>
    /// Not fullscreen, all panels and titlebar visible.
    /// </summary>
    Windowed,

    /// <summary>
    /// Fullscreen with all panels and titlebar visible.
    /// </summary>
    FullScreen,

    /// <summary>
    /// Fullscreen, only the documents panel (including tab bar) is visible.
    /// </summary>
    ZenMode,

    /// <summary>
    /// Fullscreen with only the selected document content visible (no tab bar).
    /// </summary>
    Presenter
}

/// <summary>
/// A requested window mode transition.
/// </summary>
public enum WindowModeTransition
{
    /// <summary>
    /// Enter standard windowed mode.
    /// </summary>
    EnterWindowed,

    /// <summary>
    /// Enter fullscreen mode.
    /// </summary>
    EnterFullScreen,

    /// <summary>
    /// Enter fullscreen mode with all panels hidden (zen/focus mode).
    /// </summary>
    EnterZenMode,

    /// <summary>
    /// Enter fullscreen mode with only document content visible (no document tabs or panels).
    /// </summary>
    EnterPresenterMode,

    /// <summary>
    /// Toggles between Windowed mode and Zen Mode (fullscreen with all panels hidden).
    /// </summary>
    ToggleZenMode,

    /// <summary>
    /// Restore all panels to visible and reset their sizes to defaults.
    /// Also enters Windowed mode if currently in a fullscreen mode.
    /// </summary>
    ResetLayout
}

