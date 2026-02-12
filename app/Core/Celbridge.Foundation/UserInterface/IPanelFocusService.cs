namespace Celbridge.UserInterface;

/// <summary>
/// Identifies the focusable panels in the workspace.
/// </summary>
public enum FocusablePanel
{
    None,
    Explorer,
    Search,
    Inspector,
    Console,
    Documents
}

/// <summary>
/// Manages focus state across panels to ensure only one panel appears focused at a time.
/// </summary>
public interface IPanelFocusService
{
    /// <summary>
    /// Gets the currently focused panel.
    /// </summary>
    FocusablePanel FocusedPanel { get; }

    /// <summary>
    /// Sets the focused panel and notifies all panels of the change.
    /// </summary>
    void SetFocusedPanel(FocusablePanel panel);

    /// <summary>
    /// Clears the focused panel (sets to None).
    /// </summary>
    void ClearFocus();
}
