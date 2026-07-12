namespace Celbridge.Workspace;

/// <summary>
/// Tracks which workspace panel holds focus so that only one panel appears focused at a time, and
/// coordinates release of focus from the surface that is losing it. Panel focus and edit context are
/// distinct: panel focus follows the caret, while the edit context follows the surface that Edit commands
/// should act on and survives focus moving onto chrome.
/// </summary>
public interface IFocusService
{
    /// <summary>
    /// The panel that currently holds focus, or None when focus has left the workspace panels (for example
    /// onto a toolbar or another chrome element).
    /// </summary>
    WorkspacePanel FocusedPanel { get; }

    /// <summary>
    /// The surface that Edit commands route to, or null before any surface has claimed one. Preserved when
    /// focus moves onto chrome or clears, so Edit commands still target the last editing surface; replaced
    /// when a new surface claims focus with a target; cleared when its surface is torn down.
    /// </summary>
    IEditTarget? EditTarget { get; }

    /// <summary>
    /// Handles a panel receiving focus: records it as the focused panel and invokes the previous surface's
    /// release callback. A claim that carries a target replaces the edit target; a target-less claim leaves
    /// the edit target in place. Both target and onReleaseFocus are optional.
    /// </summary>
    void OnFocusReceived(WorkspacePanel panel, IEditTarget? target = null, Action? onReleaseFocus = null);

    /// <summary>
    /// Clears the focused panel to None and releases the surface that holds the caret. The edit context is
    /// preserved, so Edit commands still route to the last editing surface.
    /// </summary>
    void ClearFocus();

    /// <summary>
    /// Clears the edit target if it still references the given surface, so a surface being torn down stops
    /// receiving Edit commands. A newer target is left in place.
    /// </summary>
    void ClearEditTarget(IEditTarget target);

    /// <summary>
    /// Registers how the given panel takes keyboard focus, or null to clear it. Used to return keyboard
    /// focus to the focused panel after an interaction moves it away transiently (a modal dialog closing,
    /// a resource-tree rebuild).
    /// </summary>
    void SetPanelFocusHandler(WorkspacePanel panel, Action? focusHandler);

    /// <summary>
    /// Re-asserts keyboard focus on the currently focused panel by invoking its registered focus handler,
    /// so the panel the focus indicator shows becomes the keyboard target again. A no-op when the focused
    /// panel has no registered handler.
    /// </summary>
    void RefocusFocusedPanel();
}
