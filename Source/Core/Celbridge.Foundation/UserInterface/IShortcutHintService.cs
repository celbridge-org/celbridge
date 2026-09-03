using Celbridge.Workspace;

namespace Celbridge.UserInterface;

/// <summary>
/// Provides the display form of the chord that performs a shortcut, read from the string resources. The
/// text is display only and creates no key binding.
/// </summary>
public interface IShortcutHintService
{
    /// <summary>
    /// The chord that performs the edit verb on the current platform.
    /// </summary>
    string GetText(EditIntent intent);

    /// <summary>
    /// The chord that performs the named shortcut on the current platform. The name is the shared prefix of
    /// its two resource entries, suffixed Command on macOS and Control elsewhere.
    /// </summary>
    string GetText(string shortcutName);
}
