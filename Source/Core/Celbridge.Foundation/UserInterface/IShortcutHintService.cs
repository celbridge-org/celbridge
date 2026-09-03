using Celbridge.Workspace;

namespace Celbridge.UserInterface;

/// <summary>
/// The display form of the chord that performs a shortcut, read from the string resources. Menu items show
/// this beside their label. It is display only and binds nothing.
/// </summary>
public interface IShortcutHintService
{
    /// <summary>
    /// The chord that performs the edit verb on the current platform.
    /// </summary>
    string GetText(EditIntent intent);

    /// <summary>
    /// The chord that performs the named shortcut on the current platform. The name is what its two resource
    /// entries share, so "DocumentTab_CloseShortcut" resolves DocumentTab_CloseShortcutCommand on macOS and
    /// DocumentTab_CloseShortcutControl elsewhere.
    /// </summary>
    string GetText(string shortcutName);
}
