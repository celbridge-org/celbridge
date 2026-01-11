namespace Celbridge.Settings;

/// <summary>
/// Defines the 4 mutually exclusive window layouts for the application.
/// </summary>
public enum WindowLayout
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
