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
    /// Context-aware toggle: cycles between Windowed and ZenMode.
    /// If in Windowed mode with all panels collapsed, restores all panels instead.
    /// If in any fullscreen mode, returns to Windowed.
    /// </summary>
    ToggleZenMode,

    /// <summary>
    /// Restore all panels to visible and reset their sizes to defaults.
    /// Also enters Windowed mode if currently in a fullscreen mode.
    /// </summary>
    ResetLayout
}
