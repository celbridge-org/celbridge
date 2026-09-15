namespace Celbridge.UserInterface;

/// <summary>
/// Where a focused element sits relative to the window's content.
/// </summary>
public enum FocusLocation
{
    /// <summary>
    /// The window's main content.
    /// </summary>
    MainContent,

    /// <summary>
    /// An open popup: a flyout, a context menu or a content dialog.
    /// </summary>
    Popup,

    /// <summary>
    /// Outside the window's tree entirely, on an element the user can no longer see or reach.
    /// </summary>
    Detached
}
