using Celbridge.Search.Services;

namespace Celbridge.Tests.Search;

[TestFixture]
public class TextMatcherTests
{
    private TextMatcher _matcher = null!;

    [SetUp]
    public void SetUp()
    {
        _matcher = new TextMatcher();
    }

    [Test]
    public void FindMatches_SimpleMatch_ReturnsCorrectPosition()
    {
        var matches = _matcher.FindMatches("Hello world", "world", matchCase: false, wholeWord: false);

        matches.Should().HaveCount(1);
        matches[0].Start.Should().Be(6);
        matches[0].Length.Should().Be(5);
    }

    [Test]
    public void FindMatches_CaseSensitive_MatchesCorrectCase()
    {
        var matches = _matcher.FindMatches("Hello World", "world", matchCase: true, wholeWord: false);

        matches.Should().BeEmpty();
    }

    [Test]
    public void FindMatches_CaseInsensitive_FindsMatch()
    {
        var matches = _matcher.FindMatches("Hello World", "world", matchCase: false, wholeWord: false);

        matches.Should().HaveCount(1);
        matches[0].Start.Should().Be(6);
    }

    [Test]
    public void FindMatches_WholeWord_OnlyMatchesWholeWords()
    {
        var matches = _matcher.FindMatches("worldwide world", "world", matchCase: false, wholeWord: true);

        matches.Should().HaveCount(1);
        matches[0].Start.Should().Be(10);
    }

    [Test]
    public void FindMatches_WholeWordAtStart_Matches()
    {
        var matches = _matcher.FindMatches("world is wide", "world", matchCase: false, wholeWord: true);

        matches.Should().HaveCount(1);
        matches[0].Start.Should().Be(0);
    }

    [Test]
    public void FindMatches_WholeWordAtEnd_Matches()
    {
        var matches = _matcher.FindMatches("hello world", "world", matchCase: false, wholeWord: true);

        matches.Should().HaveCount(1);
        matches[0].Start.Should().Be(6);
    }

    [Test]
    public void FindMatches_MultipleMatches_FindsAll()
    {
        var matches = _matcher.FindMatches("the cat and the dog", "the", matchCase: false, wholeWord: false);

        matches.Should().HaveCount(2);
        matches[0].Start.Should().Be(0);
        matches[1].Start.Should().Be(12);
    }

    [Test]
    public void FindMatches_OverlappingMatches_FindsAll()
    {
        var matches = _matcher.FindMatches("aaa", "aa", matchCase: false, wholeWord: false);

        matches.Should().HaveCount(2);
        matches[0].Start.Should().Be(0);
        matches[1].Start.Should().Be(1);
    }

    [Test]
    public void FindMatches_NoMatch_ReturnsEmpty()
    {
        var matches = _matcher.FindMatches("Hello world", "goodbye", matchCase: false, wholeWord: false);

        matches.Should().BeEmpty();
    }

    [Test]
    public void FindMatches_EmptyLine_ReturnsEmpty()
    {
        var matches = _matcher.FindMatches("", "test", matchCase: false, wholeWord: false);

        matches.Should().BeEmpty();
    }
}
