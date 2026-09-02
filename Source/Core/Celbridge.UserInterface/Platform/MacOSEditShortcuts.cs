using Celbridge.Workspace;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// One standard edit verb: the chord that names it, the intent it performs, and the selector and label its
/// menu item carries. Group orders the items into the Edit menu's separated blocks.
/// </summary>
internal sealed record MacOSEditShortcut(
    char Character,
    bool Shift,
    EditIntent Intent,
    string SelectorName,
    string LabelKey,
    int Group);

/// <summary>
/// The standard edit verbs and the chord each carries, declared once. macOS-only.
/// </summary>
internal static class MacOSEditShortcuts
{
    public static IReadOnlyList<MacOSEditShortcut> All { get; } = new MacOSEditShortcut[]
    {
        new('z', false, EditIntent.Undo, "undo:", "Menu_Undo", 0),
        new('z', true, EditIntent.Redo, "redo:", "Menu_Redo", 0),
        new('x', false, EditIntent.Cut, "cut:", "Menu_Cut", 1),
        new('c', false, EditIntent.Copy, "copy:", "Menu_Copy", 1),
        new('v', false, EditIntent.Paste, "paste:", "Menu_Paste", 1),
        new('a', false, EditIntent.SelectAll, "selectAll:", "Menu_SelectAll", 1)
    };

    /// <summary>
    /// The verb the given chord names, or null when it names none. The character must already be resolved to
    /// an ASCII letter.
    /// </summary>
    public static EditIntent? ResolveIntent(char? character, bool shift)
    {
        if (character is null)
        {
            return null;
        }

        foreach (var shortcut in All)
        {
            if (shortcut.Character == character.Value
                && shortcut.Shift == shift)
            {
                return shortcut.Intent;
            }
        }

        return null;
    }
}
