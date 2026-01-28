using Celbridge.Search.Services;

namespace Celbridge.Tests.Search;

[TestFixture]
public class SearchResultFormatterTests
{
    private SearchResultFormatter _formatter = null!;

    [SetUp]
    public void SetUp()
    {
        _formatter = new SearchResultFormatter();
    }

    [Test]
    public void FormatContextLine_ShortLine_ReturnsWholeLine()
    {
        var (displayText, matchStart) = _formatter.FormatContextLine("Hello world", 6, 5);

        displayText.Should().Be("Hello world");
        matchStart.Should().Be(6);
    }

    [Test]
    public void FormatContextLine_LeadingWhitespace_TrimsAndAdjusts()
    {
        var (displayText, matchStart) = _formatter.FormatContextLine("    Hello world", 10, 5);

        displayText.Should().Be("Hello world");
        matchStart.Should().Be(6);
    }

    [Test]
    public void FormatContextLine_TrailingWhitespace_Trims()
    {
        var (displayText, matchStart) = _formatter.FormatContextLine("Hello world    ", 6, 5);

        displayText.Should().Be("Hello world");
        matchStart.Should().Be(6);
    }

    [Test]
    public void FormatContextLine_MatchNearStart_NoPrefix()
    {
        var line = "This is a test line with some content";
        var (displayText, matchStart) = _formatter.FormatContextLine(line, 10, 4);

        displayText.Should().NotStartWith("...");
        matchStart.Should().BeLessThan(30);
    }

    [Test]
    public void FormatContextLine_MatchFarFromStart_AddsPrefixEllipsis()
    {
        var line = "This is a very long line that has the match word somewhere in the middle of the text content";
        var (displayText, matchStart) = _formatter.FormatContextLine(line, 55, 4);

        displayText.Should().StartWith("...");
    }

    [Test]
    public void FormatContextLine_LongLine_TruncatesWithSuffix()
    {
        var line = "This is a very long line that contains many words and will definitely exceed the maximum display length limit";
        var (displayText, matchStart) = _formatter.FormatContextLine(line, 15, 4);

        displayText.Should().EndWith("...");
        (displayText.Length <= 103).Should().BeTrue(); // Max length + ellipsis
    }

    [Test]
    public void FormatContextLine_MatchAtStartOfLine_NoPrefix()
    {
        var line = "match is at the start of this line";
        var (displayText, matchStart) = _formatter.FormatContextLine(line, 0, 5);

        displayText.Should().Be("match is at the start of this line");
        matchStart.Should().Be(0);
    }

    [Test]
    public void FormatContextLine_MatchAtEndOfLongLine_AddsPrefixOnly()
    {
        var line = "This is a very long line that has content and then the match word is here at the end_match";
        var (displayText, matchStart) = _formatter.FormatContextLine(line, 86, 5);

        displayText.Should().StartWith("...");
        displayText.Should().Contain("match");
    }

    [Test]
    public void FormatContextLine_MatchPositionValid_WithinDisplayText()
    {
        var line = "Some text before the match word and some text after";
        var (displayText, matchStart) = _formatter.FormatContextLine(line, 21, 5);

        (matchStart >= 0).Should().BeTrue();
        (matchStart + 5 <= displayText.Length).Should().BeTrue();
        displayText.Substring(matchStart, 5).Should().BeEquivalentTo("match");
    }
}
