using Celbridge.UserInterface.Platform;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Unit tests for recognising the Control chords that would type a control character. The decision reads only
/// the key event's fields, so these run on every platform.
/// </summary>
[TestFixture]
public class MacOSControlChordsTests
{
    private const ulong Control = MacOSKeyboardModifiers.ControlFlag;
    private const ulong Command = MacOSKeyboardModifiers.CommandFlag;
    private const ulong Shift = MacOSKeyboardModifiers.ShiftFlag;
    private const ulong Option = MacOSKeyboardModifiers.OptionFlag;

    // Key codes of the keys the cases press: G, X, A, E, K and H.
    private const ulong GKeyCode = 5;
    private const ulong XKeyCode = 7;
    private const ulong AKeyCode = 0;
    private const ulong EKeyCode = 14;
    private const ulong KKeyCode = 40;
    private const ulong HKeyCode = 4;

    // The characters AppKit reports for each chord are the ones Uno typed into the box.
    [TestCase(GKeyCode, "\u0007")]
    [TestCase(XKeyCode, "\u0018")]
    [TestCase(AKeyCode, "\u0001")]
    [TestCase(EKeyCode, "\u0005")]
    [TestCase(KKeyCode, "\u000B")]
    [TestCase(HKeyCode, "\b")]
    public void TypesControlCharacter_ForAControlLetter_ReturnsTrue(ulong keyCode, string characters)
    {
        MacOSControlChords.TypesControlCharacter(Control, keyCode, characters).Should().BeTrue();
    }

    [Test]
    public void TypesControlCharacter_WithShiftOrOptionAlsoHeld_ReturnsTrue()
    {
        MacOSControlChords.TypesControlCharacter(Control | Shift, GKeyCode, "\u0007").Should().BeTrue();
        MacOSControlChords.TypesControlCharacter(Control | Option, GKeyCode, "\u0007").Should().BeTrue();
    }

    // A text control acts on these keys whatever modifier is held, so Control never makes them junk.
    [TestCase(48ul, "\t")]
    [TestCase(36ul, "\r")]
    [TestCase(51ul, "\u007F")]
    [TestCase(53ul, "\u001B")]
    [TestCase(76ul, "\u0003")]
    public void TypesControlCharacter_ForAKeyATextControlActsOn_ReturnsFalse(ulong keyCode, string characters)
    {
        MacOSControlChords.TypesControlCharacter(Control, keyCode, characters).Should().BeFalse();
    }

    [Test]
    public void TypesControlCharacter_WithCommandHeld_ReturnsFalse()
    {
        // Command chords belong to the shortcuts and the edit verbs, which the key monitor routes separately.
        MacOSControlChords.TypesControlCharacter(Control | Command, GKeyCode, "\u0007").Should().BeFalse();
    }

    [Test]
    public void TypesControlCharacter_WithoutControl_ReturnsFalse()
    {
        MacOSControlChords.TypesControlCharacter(0, GKeyCode, "g").Should().BeFalse();
        MacOSControlChords.TypesControlCharacter(Shift, GKeyCode, "G").Should().BeFalse();
    }

    [Test]
    public void TypesControlCharacter_ForAPrintableCharacter_ReturnsFalse()
    {
        // Control with an arrow reports a private use character, and some layouts give Control chords a
        // printable character. Neither types junk.
        MacOSControlChords.TypesControlCharacter(Control, 123, "").Should().BeFalse();
        MacOSControlChords.TypesControlCharacter(Control, 50, "`").Should().BeFalse();
    }

    [Test]
    public void TypesControlCharacter_ForNoOrSeveralCharacters_ReturnsFalse()
    {
        // A dead key reports no characters, and a chord that composes reports more than one.
        MacOSControlChords.TypesControlCharacter(Control, GKeyCode, string.Empty).Should().BeFalse();
        MacOSControlChords.TypesControlCharacter(Control, GKeyCode, "\u0007\u0007").Should().BeFalse();
    }
}
