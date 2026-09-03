using Celbridge.Platform;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// The display form of the chord that performs an edit verb, written the way the current platform writes
/// it. Menu items show this beside their label. It is display only and binds nothing.
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

            // Windows spells redo Ctrl+Y, other platforms use the Shift chord.
            EditIntent.Redo => platformInfo.TreatsCtrlYAsRedo
                ? "Ctrl+Y"
                : Chord(usesCommandModifier, "Z", shift: true),

            EditIntent.Cut => Chord(usesCommandModifier, "X"),
            EditIntent.Copy => Chord(usesCommandModifier, "C"),
            EditIntent.Paste => Chord(usesCommandModifier, "V"),
            EditIntent.SelectAll => Chord(usesCommandModifier, "A"),
            EditIntent.Duplicate => Chord(usesCommandModifier, "D"),

            // No modifier. macOS labels this key Backspace and shows the ⌫ glyph.
            EditIntent.Delete => platformInfo.TreatsBackspaceAsDeleteKey ? "⌫" : "Del",

            EditIntent.Rename => "F2",
            _ => null
        };
    }

    private static string Chord(bool usesCommandModifier, string key, bool shift = false)
    {
        if (usesCommandModifier)
        {
            return (shift ? "⇧⌘" : "⌘") + key;
        }

        return shift ? $"Ctrl+Shift+{key}" : $"Ctrl+{key}";
    }
}
