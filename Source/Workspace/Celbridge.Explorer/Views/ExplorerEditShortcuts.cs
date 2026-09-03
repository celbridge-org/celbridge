using Celbridge.Workspace;
using Windows.System;

namespace Celbridge.Explorer.Views;

/// <summary>
/// One command-modifier chord the resource tree answers to directly: the key, whether Shift is held, and
/// the verb it performs. Delete and Rename are not chords (no command modifier) and stay outside this table.
/// </summary>
internal sealed partial record ExplorerEditShortcut(VirtualKey Key, bool Shift, EditIntent Intent);

/// <summary>
/// The command-modifier chords the resource tree answers to directly, declared once.
/// </summary>
internal static class ExplorerEditShortcuts
{
    public static IReadOnlyList<ExplorerEditShortcut> All { get; } = new ExplorerEditShortcut[]
    {
        new(VirtualKey.Z, false, EditIntent.Undo),
        new(VirtualKey.Z, true, EditIntent.Redo),
        new(VirtualKey.A, false, EditIntent.SelectAll),
        new(VirtualKey.D, false, EditIntent.Duplicate),
        new(VirtualKey.C, false, EditIntent.Copy),
        new(VirtualKey.X, false, EditIntent.Cut),
        new(VirtualKey.V, false, EditIntent.Paste)
    };

    /// <summary>
    /// The verb the given chord names, or null when it names none.
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
