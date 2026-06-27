namespace Celbridge.Workspace;

/// <summary>
/// Tracks which workspace panel holds focus so that only one panel appears focused at a time, and
/// coordinates release of focus from the surface that is losing it.
/// </summary>
public interface IFocusService
{
    /// <summary>
    /// The panel that currently holds focus, or None when nothing is focused.
    /// </summary>
    WorkspacePanel FocusedPanel { get; }

    /// <summary>
    /// The edit target for the focused surface, or null when the focused surface performs no edit verbs.
    /// The edit-intent router and the menus dispatch to this target.
    /// </summary>
    IEditTarget? EditTarget { get; }

    /// <summary>
    /// Handles a panel receiving focus: records it as the focused panel, along with the surface's edit
    /// target and a callback to release its focus. The service invokes onReleaseFocus when focus later
    /// moves to a different panel. Both target and onReleaseFocus are optional.
    /// </summary>
    void OnFocusReceived(WorkspacePanel panel, IEditTarget? target = null, Action? onReleaseFocus = null);

    /// <summary>
    /// Clears the focused panel, releasing focus from the surface that currently holds it.
    /// </summary>
    void ClearFocus();
}
