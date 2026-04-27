using Celbridge.Utilities;

namespace Celbridge.Tests.Utilities;

/// <summary>
/// Tests for LineEndingHelper — line-ending detection, content splitting, and
/// canonical line counting used by all document-aware tools.
/// </summary>
[TestFixture]
public class LineEndingHelperTests
{
    [Test]
    public void DetectSeparatorOrDefault_EmptyContent_ReturnsPlatformDefault()
    {
        LineEndingHelper.DetectSeparatorOrDefault(string.Empty)
            .Should().Be(LineEndingHelper.PlatformDefault);
    }

    [Test]
    public void DetectSeparatorOrDefault_PureLf_ReturnsLf()
    {
        LineEndingHelper.DetectSeparatorOrDefault("a\nb\nc").Should().Be("\n");
    }

    [Test]
    public void DetectSeparatorOrDefault_PureCrLf_ReturnsCrLf()
    {
        LineEndingHelper.DetectSeparatorOrDefault("a\r\nb\r\nc").Should().Be("\r\n");
    }

    [Test]
    public void DetectSeparatorOrDefault_MixedFavoursDominantStyle()
    {
        // Two CRLF lines and one lone LF line — CRLF wins.
        LineEndingHelper.DetectSeparatorOrDefault("a\r\nb\r\nc\nd").Should().Be("\r\n");
    }

    [Test]
    public void DetectSeparatorOrDefault_SingleLine_ReturnsPlatformDefault()
    {
        LineEndingHelper.DetectSeparatorOrDefault("solo")
            .Should().Be(LineEndingHelper.PlatformDefault);
    }

    [Test]
    public void EndsWithNewline_TrueForLfTerminated()
    {
        LineEndingHelper.EndsWithNewline("line\n").Should().BeTrue();
    }

    [Test]
    public void EndsWithNewline_TrueForCrLfTerminated()
    {
        LineEndingHelper.EndsWithNewline("line\r\n").Should().BeTrue();
    }

    [Test]
    public void EndsWithNewline_FalseForUnterminatedContent()
    {
        LineEndingHelper.EndsWithNewline("no newline here").Should().BeFalse();
    }

    [Test]
    public void EndsWithNewline_FalseForEmpty()
    {
        LineEndingHelper.EndsWithNewline(string.Empty).Should().BeFalse();
    }

    [Test]
    public void SplitToContentLines_StripsTrailingCrAndDropsTerminatingNewline()
    {
        LineEndingHelper.SplitToContentLines("a\r\nb\r\n")
            .Should().Equal("a", "b");
    }

    [Test]
    public void SplitToContentLines_PreservesEmptyLinesInMiddle()
    {
        LineEndingHelper.SplitToContentLines("a\n\nb")
            .Should().Equal("a", "", "b");
    }

    [Test]
    public void SplitToContentLines_EmptyContent_ReturnsEmpty()
    {
        LineEndingHelper.SplitToContentLines(string.Empty)
            .Should().BeEmpty();
    }

    [Test]
    public void SplitToContentLines_UnterminatedContent_RetainsAllLines()
    {
        LineEndingHelper.SplitToContentLines("a\nb\nc")
            .Should().Equal("a", "b", "c");
    }

    [Test]
    public void ConvertLineEndings_LfToCrLf()
    {
        LineEndingHelper.ConvertLineEndings("a\nb\nc", "\r\n")
            .Should().Be("a\r\nb\r\nc");
    }

    [Test]
    public void ConvertLineEndings_CrLfToLf()
    {
        LineEndingHelper.ConvertLineEndings("a\r\nb\r\nc", "\n")
            .Should().Be("a\nb\nc");
    }

    [Test]
    public void ConvertLineEndings_CrLfToCrLf_DoesNotDoubleReplace()
    {
        // Regression for the trap "\n -> \r\n -> \r\r\n" when input already
        // contains \r\n. The helper must collapse first, then expand.
        LineEndingHelper.ConvertLineEndings("a\r\nb\r\nc", "\r\n")
            .Should().Be("a\r\nb\r\nc");
    }

    [Test]
    public void ConvertLineEndings_MixedInput_ProducesUniformOutput()
    {
        // Input has \n, \r\n, and a lone \r. Output must use the target separator everywhere.
        LineEndingHelper.ConvertLineEndings("a\nb\r\nc\rd", "\r\n")
            .Should().Be("a\r\nb\r\nc\r\nd");
    }

    [Test]
    public void ConvertLineEndings_LoneCr_TreatedAsLineEnding()
    {
        LineEndingHelper.ConvertLineEndings("a\rb\rc", "\n")
            .Should().Be("a\nb\nc");
    }

    [Test]
    public void ConvertLineEndings_PreservesTrailingNewline()
    {
        LineEndingHelper.ConvertLineEndings("a\nb\n", "\r\n")
            .Should().Be("a\r\nb\r\n");
    }

    [Test]
    public void ConvertLineEndings_EmptyInput_ReturnsEmpty()
    {
        LineEndingHelper.ConvertLineEndings(string.Empty, "\r\n")
            .Should().BeEmpty();
    }

    [Test]
    public void ConvertLineEndings_NoLineBreaks_ReturnsUnchanged()
    {
        LineEndingHelper.ConvertLineEndings("solo", "\r\n")
            .Should().Be("solo");
    }

    [Test]
    public void CountLines_EmptyContent_IsZero()
    {
        LineEndingHelper.CountLines(string.Empty).Should().Be(0);
    }

    [Test]
    public void CountLines_SingleLineWithoutNewline_IsOne()
    {
        LineEndingHelper.CountLines("hello").Should().Be(1);
    }

    [Test]
    public void CountLines_TrailingNewlineDoesNotAddPhantomLine()
    {
        // "x\ny\n" is 2 logical lines (matches File.ReadAllLines).
        LineEndingHelper.CountLines("x\ny\n").Should().Be(2);
    }

    [Test]
    public void CountLines_NoTrailingNewline_CountsAllLines()
    {
        LineEndingHelper.CountLines("x\ny\nz").Should().Be(3);
    }

    [Test]
    public void CountLines_CrLf_BehavesLikeLf()
    {
        LineEndingHelper.CountLines("x\r\ny\r\nz\r\n").Should().Be(3);
    }
}
