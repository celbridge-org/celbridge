using Celbridge.Explorer.Views;
using Celbridge.UserInterface.Helpers;
using Celbridge.Workspace;
using Windows.System;

namespace Celbridge.Tests.Explorer;

/// <summary>
/// Unit tests for the resource tree's chords: Duplicate, plus the standard edit chords it shares with the window.
/// </summary>
[TestFixture]
public class ExplorerEditShortcutsTests
{
    [Test]
    public void ResolveIntent_ForTheDuplicateChord_ReturnsDuplicate()
    {
        ExplorerEditShortcuts.ResolveIntent(VirtualKey.D, shift: false, treatsCtrlYAsRedo: false)
            .Should().Be(EditIntent.Duplicate);
    }

    [Test]
    public void ResolveIntent_ForTheShiftedDuplicateChord_ReturnsNull()
    {
        ExplorerEditShortcuts.ResolveIntent(VirtualKey.D, shift: true, treatsCtrlYAsRedo: false).Should().BeNull();
    }

    [TestCase(VirtualKey.Z, false, false, EditIntent.Undo)]
    [TestCase(VirtualKey.Z, true, false, EditIntent.Redo)]
    [TestCase(VirtualKey.C, false, false, EditIntent.Copy)]
    [TestCase(VirtualKey.Y, false, true, EditIntent.Redo)]
    public void ResolveIntent_ForAStandardChord_ReturnsItsVerb(
        VirtualKey key, bool shift, bool treatsCtrlYAsRedo, EditIntent expected)
    {
        ExplorerEditShortcuts.ResolveIntent(key, shift, treatsCtrlYAsRedo).Should().Be(expected);
    }

    [Test]
    public void TheDuplicateChord_IsNotAStandardChord()
    {
        var duplicate = ExplorerEditShortcuts.Duplicate;

        EditShortcuts.All.Should().NotContain(shortcut => shortcut.Key == duplicate.Key && shortcut.Shift == duplicate.Shift);
    }
}
