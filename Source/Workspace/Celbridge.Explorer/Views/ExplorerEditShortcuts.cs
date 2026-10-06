using Celbridge.UserInterface.Helpers;
using Celbridge.Workspace;
using Windows.System;

namespace Celbridge.Explorer.Views;

/// <summary>
/// The command-modifier chords the resource tree handles itself: the standard edit chords plus Duplicate.
/// Delete and Rename aren't chords, so they aren't in this table.
/// </summary>
internal static class ExplorerEditShortcuts
{
    public static EditShortcut Duplicate { get; } = new(VirtualKey.D, false, EditIntent.Duplicate);

    /// <summary>
    /// Returns the verb the chord performs in the tree, or null if it performs none.
    /// </summary>
    public static EditIntent? ResolveIntent(VirtualKey key, bool shift, bool treatsCtrlYAsRedo)
    {
        if (key == Duplicate.Key
            && shift == Duplicate.Shift)
        {
            return Duplicate.Intent;
        }

        return EditShortcuts.ResolveIntent(key, shift, treatsCtrlYAsRedo);
    }
}
