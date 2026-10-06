using Celbridge.Workspace;
using Windows.System;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// A command-modifier chord for an edit verb: the key, whether Shift is held, and the verb it performs.
/// </summary>
public sealed partial record EditShortcut(VirtualKey Key, bool Shift, EditIntent Intent);

/// <summary>
/// The command-modifier chords for the standard edit verbs. The window and the Explorer's tree both look up
/// key presses in this table.
/// </summary>
public static class EditShortcuts
{
    public static IReadOnlyList<EditShortcut> All { get; } = new EditShortcut[]
    {
        new(VirtualKey.Z, false, EditIntent.Undo),
        new(VirtualKey.Z, true, EditIntent.Redo),
        new(VirtualKey.A, false, EditIntent.SelectAll),
        new(VirtualKey.C, false, EditIntent.Copy),
        new(VirtualKey.X, false, EditIntent.Cut),
        new(VirtualKey.V, false, EditIntent.Paste)
    };

    /// <summary>
    /// Returns the verb the chord performs, or null if it performs none. Ctrl+Y is Redo only on platforms that
    /// treat it that way.
    /// </summary>
    public static EditIntent? ResolveIntent(VirtualKey key, bool shift, bool treatsCtrlYAsRedo)
    {
        if (treatsCtrlYAsRedo
            && key == VirtualKey.Y
            && !shift)
        {
            return EditIntent.Redo;
        }

        foreach (var shortcut in All)
        {
            if (shortcut.Key == key
                && shortcut.Shift == shift)
            {
                return shortcut.Intent;
            }
        }

        return null;
    }
}
