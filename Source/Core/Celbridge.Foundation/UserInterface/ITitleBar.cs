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

    /// <summary>
    /// Builds and displays the utility launcher buttons in the title bar.
    /// When a button is clicked, the callback is invoked with the utility's fully-qualified id.
    /// Returns true when at least one button was built.
    /// </summary>
    bool BuildUtilityButtons(IReadOnlyList<UtilityButton> utilities, Action<string> onOpenUtility);

    /// <summary>
    /// Clears all utility launcher buttons from the title bar.
    /// </summary>
    void ClearUtilityButtons();
}
