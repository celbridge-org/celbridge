using Celbridge.UserInterface.Platform;
using Celbridge.Workspace;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Unit tests for the standard macOS edit chords. Resolution is a pure mapping from an ASCII letter to an
/// edit verb, so these run on every platform.
/// </summary>
[TestFixture]
public class MacOSEditShortcutsTests
{
    [TestCase('z', false, EditIntent.Undo)]
    [TestCase('z', true, EditIntent.Redo)]
    [TestCase('x', false, EditIntent.Cut)]
    [TestCase('c', false, EditIntent.Copy)]
    [TestCase('v', false, EditIntent.Paste)]
    [TestCase('a', false, EditIntent.SelectAll)]
    public void ResolveIntent_ForADeclaredChord_ReturnsItsVerb(char character, bool shift, EditIntent expected)
    {
        MacOSEditShortcuts.ResolveIntent(character, shift).Should().Be(expected);
    }

    // Shift separates Undo from Redo, so the other verbs must not answer to their shifted chord.
    [TestCase('x')]
    [TestCase('c')]
    [TestCase('v')]
    [TestCase('a')]
    public void ResolveIntent_ForAShiftedChordThatNamesNoVerb_ReturnsNull(char character)
    {
        MacOSEditShortcuts.ResolveIntent(character, shift: true).Should().BeNull();
    }

    [Test]
    public void ResolveIntent_ForALetterNamingNoVerb_ReturnsNull()
    {
        // The key monitor handles W and F itself, so they must not resolve to an edit verb.
        MacOSEditShortcuts.ResolveIntent('w', shift: false).Should().BeNull();
        MacOSEditShortcuts.ResolveIntent('f', shift: false).Should().BeNull();
    }

    [Test]
    public void ResolveIntent_ForNoLetter_ReturnsNull()
    {
        MacOSEditShortcuts.ResolveIntent(null, shift: false).Should().BeNull();
    }

    [Test]
    public void EveryChordIsDeclaredOnce()
    {
        // A duplicate chord would make the resolved verb depend on list order.
        var chords = MacOSEditShortcuts.All
            .Select(shortcut => (shortcut.Character, shortcut.Shift))
            .ToList();

        chords.Should().OnlyHaveUniqueItems();
    }

    [Test]
    public void EveryDeclaredShortcutCarriesALetterAndAnAction()
    {
        // The character becomes the menu item's key equivalent and the selector its action, so a menu item
        // missing either can never fire.
        foreach (var shortcut in MacOSEditShortcuts.All)
        {
            shortcut.Character.Should().BeInRange('a', 'z');
            shortcut.SelectorName.Should().NotBeNullOrWhiteSpace();
            shortcut.LabelKey.Should().NotBeNullOrWhiteSpace();
        }
    }
}
