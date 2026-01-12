namespace Celbridge.UserInterface;

/// <summary>
/// A requested layout state transition.
/// </summary>
public enum LayoutTransition
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
    /// Toggles between a windowed and a fullscreen window layout (Zen Mode).
    /// </summary>
    ToggleLayout,

    /// <summary>
    /// Restore all panels to visible and reset their sizes to defaults.
    /// Also enters Windowed mode if currently in a fullscreen mode.
    /// </summary>
    ResetLayout
}
