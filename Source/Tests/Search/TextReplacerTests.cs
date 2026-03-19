using Celbridge.Search.Services;

namespace Celbridge.Tests.Search;

[TestFixture]
public class TextReplacerTests
{
    private TextReplacer _replacer = null!;

    [SetUp]
    public void SetUp()
    {
        _replacer = new TextReplacer();
    }

    [Test]
    public void ReplaceAll_SimpleReplacement_ReplacesText()
    {
        var (result, count) = _replacer.ReplaceAll("Hello world", "world", "universe", matchCase: false, wholeWord: false);

        result.Should().Be("Hello universe");
        count.Should().Be(1);
    }

    [Test]
    public void ReplaceAll_MultipleMatches_ReplacesAll()
    {
        var (result, count) = _replacer.ReplaceAll("the cat and the dog", "the", "a", matchCase: false, wholeWord: false);

        result.Should().Be("a cat and a dog");
        count.Should().Be(2);
    }

    [Test]
    public void ReplaceAll_MultipleMatchesOnOneLine_ReplacesAllPreservingIndices()
    {
        var (result, count) = _replacer.ReplaceAll("foo bar foo baz foo", "foo", "qux", matchCase: false, wholeWord: false);

        result.Should().Be("qux bar qux baz qux");
        count.Should().Be(3);
    }

    [Test]
    public void ReplaceAll_CaseSensitive_OnlyMatchesExactCase()
    {
        var (result, count) = _replacer.ReplaceAll("Hello HELLO hello", "hello", "hi", matchCase: true, wholeWord: false);

        result.Should().Be("Hello HELLO hi");
        count.Should().Be(1);
    }

    [Test]
    public void ReplaceAll_CaseInsensitive_MatchesAllCases()
    {
        var (result, count) = _replacer.ReplaceAll("Hello HELLO hello", "hello", "hi", matchCase: false, wholeWord: false);

        result.Should().Be("hi hi hi");
        count.Should().Be(3);
    }

    [Test]
    public void ReplaceAll_WholeWord_OnlyMatchesWholeWords()
    {
        var (result, count) = _replacer.ReplaceAll("worldwide world worlds", "world", "earth", matchCase: false, wholeWord: true);

        result.Should().Be("worldwide earth worlds");
        count.Should().Be(1);
    }

    [Test]
    public void ReplaceAll_NoMatch_ReturnsOriginal()
    {
        var (result, count) = _replacer.ReplaceAll("Hello world", "goodbye", "hi", matchCase: false, wholeWord: false);

        result.Should().Be("Hello world");
        count.Should().Be(0);
    }

    [Test]
    public void ReplaceAll_EmptyContent_ReturnsEmpty()
    {
        var (result, count) = _replacer.ReplaceAll("", "test", "replacement", matchCase: false, wholeWord: false);

        result.Should().Be("");
        count.Should().Be(0);
    }

    [Test]
    public void ReplaceAll_EmptySearchText_ReturnsOriginal()
    {
        var (result, count) = _replacer.ReplaceAll("Hello world", "", "replacement", matchCase: false, wholeWord: false);

        result.Should().Be("Hello world");
        count.Should().Be(0);
    }

    [Test]
    public void ReplaceAll_MultipleLines_ReplacesAcrossLines()
    {
        var content = "Hello world\nworld is wide\ngoodbye world";
        var (result, count) = _replacer.ReplaceAll(content, "world", "earth", matchCase: false, wholeWord: false);

        result.Should().Be("Hello earth\nearth is wide\ngoodbye earth");
        count.Should().Be(3);
    }

    [Test]
    public void ReplaceAll_PreservesUnixLineEndings()
    {
        var content = "line1\nline2\nline3";
        var (result, count) = _replacer.ReplaceAll(content, "line", "row", matchCase: false, wholeWord: false);

        result.Should().Be("row1\nrow2\nrow3");
        result.Should().NotContain("\r");
    }

    [Test]
    public void ReplaceAll_PreservesWindowsLineEndings()
    {
        var content = "line1\r\nline2\r\nline3";
        var (result, count) = _replacer.ReplaceAll(content, "line", "row", matchCase: false, wholeWord: false);

        result.Should().Be("row1\r\nrow2\r\nrow3");
    }

    [Test]
    public void ReplaceAll_MixedLineEndings_PreservesEach()
    {
        var content = "line1\r\nline2\nline3\r\n";
        var (result, count) = _replacer.ReplaceAll(content, "line", "row", matchCase: false, wholeWord: false);

        result.Should().Be("row1\r\nrow2\nrow3\r\n");
    }

    [Test]
    public void ReplaceAll_ReplacementLongerThanSearch_Works()
    {
        var (result, count) = _replacer.ReplaceAll("a b a", "a", "xyz", matchCase: false, wholeWord: false);

        result.Should().Be("xyz b xyz");
        count.Should().Be(2);
    }

    [Test]
    public void ReplaceAll_ReplacementShorterThanSearch_Works()
    {
        var (result, count) = _replacer.ReplaceAll("abc def abc", "abc", "x", matchCase: false, wholeWord: false);

        result.Should().Be("x def x");
        count.Should().Be(2);
    }

    [Test]
    public void ReplaceAll_ReplaceWithEmpty_DeletesMatches()
    {
        var (result, count) = _replacer.ReplaceAll("hello world hello", "hello ", "", matchCase: false, wholeWord: false);

        result.Should().Be("world hello");
        count.Should().Be(1);
    }

    [Test]
    public void ReplaceMatch_SingleMatch_ReplacesCorrectly()
    {
        var content = "Hello world";
        var (result, success) = _replacer.ReplaceMatch(content, "world", "universe", lineNumber: 1, originalMatchStart: 6, matchCase: false, wholeWord: false);

        success.Should().BeTrue();
        result.Should().Be("Hello universe");
    }

    [Test]
    public void ReplaceMatch_MultipleMatchesOnLine_ReplacesOnlyTarget()
    {
        var content = "foo bar foo baz foo";
        var (result, success) = _replacer.ReplaceMatch(content, "foo", "qux", lineNumber: 1, originalMatchStart: 8, matchCase: false, wholeWord: false);

        success.Should().BeTrue();
        result.Should().Be("foo bar qux baz foo");
    }

    [Test]
    public void ReplaceMatch_TargetOnSpecificLine_ReplacesCorrectLine()
    {
        var content = "line one\nfoo bar\nline three";
        var (result, success) = _replacer.ReplaceMatch(content, "bar", "baz", lineNumber: 2, originalMatchStart: 4, matchCase: false, wholeWord: false);

        success.Should().BeTrue();
        result.Should().Be("line one\nfoo baz\nline three");
    }

    [Test]
    public void ReplaceMatch_InvalidLineNumber_ReturnsFalse()
    {
        var content = "Hello world";
        var (result, success) = _replacer.ReplaceMatch(content, "world", "universe", lineNumber: 5, originalMatchStart: 6, matchCase: false, wholeWord: false);

        success.Should().BeFalse();
        result.Should().Be(content);
    }

    [Test]
    public void ReplaceMatch_WrongPosition_ReturnsFalse()
    {
        var content = "Hello world";
        var (result, success) = _replacer.ReplaceMatch(content, "world", "universe", lineNumber: 1, originalMatchStart: 0, matchCase: false, wholeWord: false);

        success.Should().BeFalse();
        result.Should().Be(content);
    }

    [Test]
    public void ReplaceMatch_MatchNoLongerExists_ReturnsFalse()
    {
        var content = "Hello universe";
        var (result, success) = _replacer.ReplaceMatch(content, "world", "earth", lineNumber: 1, originalMatchStart: 6, matchCase: false, wholeWord: false);

        success.Should().BeFalse();
        result.Should().Be(content);
    }

    [Test]
    public void ReplaceMatch_PreservesLineEndings()
    {
        var content = "line1\r\nfoo bar\r\nline3";
        var (result, success) = _replacer.ReplaceMatch(content, "bar", "baz", lineNumber: 2, originalMatchStart: 4, matchCase: false, wholeWord: false);

        success.Should().BeTrue();
        result.Should().Be("line1\r\nfoo baz\r\nline3");
    }

    [Test]
    public void ReplaceMatch_CaseSensitive_MatchesExactCase()
    {
        var content = "Hello WORLD world";
        var (result, success) = _replacer.ReplaceMatch(content, "world", "earth", lineNumber: 1, originalMatchStart: 12, matchCase: true, wholeWord: false);

        success.Should().BeTrue();
        result.Should().Be("Hello WORLD earth");
    }

    [Test]
    public void ReplaceMatch_WholeWord_MatchesOnlyWholeWord()
    {
        var content = "worldwide world";
        var (result, success) = _replacer.ReplaceMatch(content, "world", "earth", lineNumber: 1, originalMatchStart: 10, matchCase: false, wholeWord: true);

        success.Should().BeTrue();
        result.Should().Be("worldwide earth");
    }
}
