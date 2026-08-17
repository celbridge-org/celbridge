using Celbridge.Resources.Helpers;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Tests that character offsets resolve to the one-based line and column numbers the document
/// navigation payload is written in.
/// </summary>
[TestFixture]
public class TextLineIndexTests
{
    [Test]
    public void OffsetOnTheFirstLine_ResolvesToLineOne()
    {
        var index = new TextLineIndex("hello world");

        index.Resolve(0).Should().Be(new TextPosition(1, 1));
        index.Resolve(6).Should().Be(new TextPosition(1, 7));
    }

    [Test]
    public void OffsetAfterANewline_ResolvesToTheNextLine()
    {
        var index = new TextLineIndex("one\ntwo\nthree");

        index.Resolve(4).Should().Be(new TextPosition(2, 1));
        index.Resolve(8).Should().Be(new TextPosition(3, 1));
        index.Resolve(10).Should().Be(new TextPosition(3, 3));
    }

    [Test]
    public void CarriageReturns_CountTowardsTheColumn()
    {
        // Lines are split on \n alone, so the \r of a CRLF pair sits at the end of the preceding line
        // and the following line still starts at column 1.
        var index = new TextLineIndex("one\r\ntwo");

        index.Resolve(3).Should().Be(new TextPosition(1, 4));
        index.Resolve(5).Should().Be(new TextPosition(2, 1));
    }

    [Test]
    public void OffsetPastTheEnd_ResolvesToTheLastLine()
    {
        var index = new TextLineIndex("one\ntwo");

        index.Resolve(99).Line.Should().Be(2);
    }
}
