namespace Celbridge.UserInterface;

/// <summary>
/// Provides access to title bar functionality that can be used by workspace-level components
/// without depending on the concrete TitleBar view.
/// </summary>
public interface ITitleBar
{
    /// <summary>
    /// Builds and displays shortcut buttons from the given shortcuts.
    /// When a shortcut is clicked, the provided callback is invoked with the script to execute.
    /// </summary>
    bool BuildShortcutButtons(IReadOnlyList<Shortcut> shortcuts, Action<string> onScriptExecute);

    /// <summary>
    /// Sets the visibility of the shortcut buttons area.
    /// </summary>
    void SetShortcutButtonsVisible(bool isVisible);

    /// <summary>
    /// Clears all shortcut buttons from the title bar.
    /// </summary>
    void ClearShortcutButtons();
}
