using Celbridge.Explorer.Views;
using Celbridge.Workspace;
using Windows.System;

namespace Celbridge.Tests.Explorer;

/// <summary>
/// Unit tests for the resource tree's command-modifier chords. Resolution is a pure mapping from a key and
/// Shift state to an edit verb, so these run on every platform.
/// </summary>
[TestFixture]
public class ExplorerEditShortcutsTests
{
    [TestCase(VirtualKey.Z, false, EditIntent.Undo)]
    [TestCase(VirtualKey.Z, true, EditIntent.Redo)]
    [TestCase(VirtualKey.A, false, EditIntent.SelectAll)]
    [TestCase(VirtualKey.D, false, EditIntent.Duplicate)]
    [TestCase(VirtualKey.C, false, EditIntent.Copy)]
    [TestCase(VirtualKey.X, false, EditIntent.Cut)]
    [TestCase(VirtualKey.V, false, EditIntent.Paste)]
    public void ResolveIntent_ForADeclaredChord_ReturnsItsVerb(VirtualKey key, bool shift, EditIntent expected)
    {
        ExplorerEditShortcuts.ResolveIntent(key, shift, treatsCtrlYAsRedo: false).Should().Be(expected);
    }

    // Shift separates Undo from Redo, so the other verbs must not answer to their shifted chord.
    [TestCase(VirtualKey.A)]
    [TestCase(VirtualKey.D)]
    [TestCase(VirtualKey.C)]
    [TestCase(VirtualKey.X)]
    [TestCase(VirtualKey.V)]
    public void ResolveIntent_ForAShiftedChordThatNamesNoVerb_ReturnsNull(VirtualKey key)
    {
        ExplorerEditShortcuts.ResolveIntent(key, shift: true, treatsCtrlYAsRedo: false).Should().BeNull();
    }

    [Test]
    public void ResolveIntent_ForAKeyNamingNoVerb_ReturnsNull()
    {
        ExplorerEditShortcuts.ResolveIntent(VirtualKey.W, shift: false, treatsCtrlYAsRedo: false).Should().BeNull();
        ExplorerEditShortcuts.ResolveIntent(VirtualKey.F2, shift: false, treatsCtrlYAsRedo: false).Should().BeNull();
    }

    [Test]
    public void ResolveIntent_ForCtrlY_ReturnsRedoOnlyWhereThePlatformTreatsItThatWay()
    {
        ExplorerEditShortcuts.ResolveIntent(VirtualKey.Y, shift: false, treatsCtrlYAsRedo: true).Should().Be(EditIntent.Redo);
        ExplorerEditShortcuts.ResolveIntent(VirtualKey.Y, shift: false, treatsCtrlYAsRedo: false).Should().BeNull();
    }

    [Test]
    public void ResolveIntent_ForCtrlShiftY_ReturnsNullRegardlessOfPlatform()
    {
        ExplorerEditShortcuts.ResolveIntent(VirtualKey.Y, shift: true, treatsCtrlYAsRedo: true).Should().BeNull();
    }

    [Test]
    public void EveryChordIsDeclaredOnce()
    {
        // A duplicate chord would make the resolved verb depend on list order.
        var chords = ExplorerEditShortcuts.All
            .Select(shortcut => (shortcut.Key, shortcut.Shift))
            .ToList();

        chords.Should().OnlyHaveUniqueItems();
    }
}
