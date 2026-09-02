using Celbridge.Platform;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// The display form of the chord that performs an edit verb, written the way the current platform writes
/// it. Menu items show this beside their label so the shortcut is discoverable from the menu.
///
/// Display only. The chords themselves are resolved by EditKeyboard and handled by the focused surface,
/// the macOS menu bar, and the key monitors, so a change to one belongs in both places.
/// </summary>
public static class EditShortcutHint
{
    /// <summary>
    /// The chord that performs the intent, or null for an intent that carries no shortcut.
    /// </summary>
    public static string? For(EditIntent intent, IPlatformInfo platformInfo)
    {
        bool usesCommandModifier = platformInfo.CommandModifier == CommandModifierKey.Command;

        return intent switch
        {
            EditIntent.Undo => Chord(usesCommandModifier, "Z"),

            // Windows spells redo Ctrl+Y, the other heads take the cross-platform Shift chord.
            EditIntent.Redo => platformInfo.TreatsCtrlYAsRedo
                ? "Ctrl+Y"
                : Chord(usesCommandModifier, "Z", shift: true),

            EditIntent.Cut => Chord(usesCommandModifier, "X"),
            EditIntent.Copy => Chord(usesCommandModifier, "C"),
            EditIntent.Paste => Chord(usesCommandModifier, "V"),
            EditIntent.SelectAll => Chord(usesCommandModifier, "A"),
            EditIntent.Duplicate => Chord(usesCommandModifier, "D"),

            // Unmodified, and named for the key the platform's keyboard labels: Backspace on macOS.
            EditIntent.Delete => platformInfo.TreatsBackspaceAsDeleteKey ? "⌫" : "Del",

            EditIntent.Rename => "F2",
            _ => null
        };
    }

    // "⇧⌘Z" on macOS, "Ctrl+Shift+Z" elsewhere.
    private static string Chord(bool usesCommandModifier, string key, bool shift = false)
    {
        if (usesCommandModifier)
        {
            return (shift ? "⇧⌘" : "⌘") + key;
        }

        return shift ? $"Ctrl+Shift+{key}" : $"Ctrl+{key}";
    }
}
