namespace Celbridge.Workspace;

/// <summary>
/// Manages focus state across panels to ensure only one panel appears focused at a time.
/// </summary>
public interface IPanelFocusService
{
    /// <summary>
    /// Gets the currently focused panel.
    /// </summary>
    WorkspacePanel FocusedPanel { get; }

    /// <summary>
    /// Sets the focused panel and notifies all panels of the change.
    /// </summary>
    void SetFocusedPanel(WorkspacePanel panel);

    /// <summary>
    /// Clears the focused panel (sets to None).
    /// </summary>
    void ClearFocus();
}
