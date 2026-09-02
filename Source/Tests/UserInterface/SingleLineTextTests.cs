using Celbridge.UserInterface.Helpers;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Unit tests for the single-line text sanitiser. Cleaning is a pure function over the text and the caret,
/// so these run without a TextBox.
/// </summary>
[TestFixture]
public class SingleLineTextTests
{
    [TestCase("a\tb", "ab")]
    [TestCase("a\r\nb", "ab")]
    [TestCase("\t", "")]
    [TestCase("line\none\ttwo", "lineonetwo")]
    public void Clean_RemovesTabsAndLineBreaks(string text, string expected)
    {
        SingleLineText.Clean(text, caret: 0).Text.Should().Be(expected);
    }

    [Test]
    public void Clean_ForTextThatNeedsNothingRemoved_ReturnsItUnchanged()
    {
        var cleaned = SingleLineText.Clean("https://example.com", caret: 5);

        cleaned.Text.Should().Be("https://example.com");
        cleaned.Caret.Should().Be(5);
    }

    [Test]
    public void Clean_ForACaretPastTheCleanedEnd_ClampsIt()
    {
        // The caret is read before the removal, so it can sit beyond the shortened text.
        var cleaned = SingleLineText.Clean("ab\t", caret: 3);

        cleaned.Text.Should().Be("ab");
        cleaned.Caret.Should().Be(2);
    }

    [Test]
    public void Clean_ForANegativeCaret_ClampsItToTheStart()
    {
        SingleLineText.Clean("ab", caret: -1).Caret.Should().Be(0);
    }
}
