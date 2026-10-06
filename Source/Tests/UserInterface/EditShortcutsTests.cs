using Celbridge.UserInterface.Helpers;
using Celbridge.Workspace;
using Windows.System;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Unit tests for the standard edit chords. Resolution is a pure mapping from a key and Shift state to an edit
/// verb, so these run on every platform.
/// </summary>
[TestFixture]
public class EditShortcutsTests
{
    [TestCase(VirtualKey.Z, false, EditIntent.Undo)]
    [TestCase(VirtualKey.Z, true, EditIntent.Redo)]
    [TestCase(VirtualKey.A, false, EditIntent.SelectAll)]
    [TestCase(VirtualKey.C, false, EditIntent.Copy)]
    [TestCase(VirtualKey.X, false, EditIntent.Cut)]
    [TestCase(VirtualKey.V, false, EditIntent.Paste)]
    public void ResolveIntent_ForADeclaredChord_ReturnsItsVerb(VirtualKey key, bool shift, EditIntent expected)
    {
        EditShortcuts.ResolveIntent(key, shift, treatsCtrlYAsRedo: false).Should().Be(expected);
    }

    // Shift separates Undo from Redo, so the other verbs must not answer to their shifted chord.
    [TestCase(VirtualKey.A)]
    [TestCase(VirtualKey.C)]
    [TestCase(VirtualKey.X)]
    [TestCase(VirtualKey.V)]
    public void ResolveIntent_ForAShiftedChordThatNamesNoVerb_ReturnsNull(VirtualKey key)
    {
        EditShortcuts.ResolveIntent(key, shift: true, treatsCtrlYAsRedo: false).Should().BeNull();
    }

    [TestCase(VirtualKey.W)]
    [TestCase(VirtualKey.F2)]
    [TestCase(VirtualKey.D)]
    public void ResolveIntent_ForAKeyNamingNoVerb_ReturnsNull(VirtualKey key)
    {
        EditShortcuts.ResolveIntent(key, shift: false, treatsCtrlYAsRedo: false).Should().BeNull();
    }

    [Test]
    public void ResolveIntent_ForCtrlY_ReturnsRedoOnlyWhereThePlatformTreatsItThatWay()
    {
        EditShortcuts.ResolveIntent(VirtualKey.Y, shift: false, treatsCtrlYAsRedo: true).Should().Be(EditIntent.Redo);
        EditShortcuts.ResolveIntent(VirtualKey.Y, shift: false, treatsCtrlYAsRedo: false).Should().BeNull();
    }

    [Test]
    public void ResolveIntent_ForCtrlShiftY_ReturnsNullRegardlessOfPlatform()
    {
        EditShortcuts.ResolveIntent(VirtualKey.Y, shift: true, treatsCtrlYAsRedo: true).Should().BeNull();
    }

    [Test]
    public void EveryChordIsDeclaredOnce()
    {
        // A duplicate chord would make the resolved verb depend on list order.
        var chords = EditShortcuts.All
            .Select(shortcut => (shortcut.Key, shortcut.Shift))
            .ToList();

        chords.Should().OnlyHaveUniqueItems();
    }
}
