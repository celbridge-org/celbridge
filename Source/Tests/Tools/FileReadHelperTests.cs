using Celbridge.Tools;

namespace Celbridge.Tests.Tools;

/// <summary>
/// Tests for FileReadHelper — line numbering and line counting helpers
/// used by the file_read MCP tool. Line counting follows ReadAllLines
/// semantics: empty content is 0 lines and a trailing terminator does not
/// add a phantom empty line.
/// </summary>
[TestFixture]
public class FileReadHelperTests
{
    [Test]
    public void AddLineNumbers_PrefixesLinesFromOne_WithSuppliedSeparator()
    {
        var lines = new[] { "# Heading", "", "Some text" };

        var result = FileReadHelper.AddLineNumbers(lines, 1, "\n");

        result.Should().Be("1: # Heading\n2: \n3: Some text");
    }

    [Test]
    public void AddLineNumbers_PreservesCrLf_WhenSourceFileUsesIt()
    {
        // Regression for the \r\r\n bug. The caller now strips trailing CR
        // (via LineEndingHelper.SplitToContentLines) and passes "\r\n" as the
        // separator, so the output is single CRLF between numbered rows.
        var lines = new[] { "first", "second", "third" };

        var result = FileReadHelper.AddLineNumbers(lines, 1, "\r\n");

        result.Should().Be("1: first\r\n2: second\r\n3: third");
        result.Should().NotContain("\r\r");
    }

    [Test]
    public void AddLineNumbers_PrefixesLinesFromOffset()
    {
        var lines = new[] { "line five", "line six", "line seven" };

        var result = FileReadHelper.AddLineNumbers(lines, 5, "\n");

        result.Should().Be("5: line five\n6: line six\n7: line seven");
    }

    [Test]
    public void AddLineNumbers_EmptyArray_ReturnsEmptyString()
    {
        var lines = Array.Empty<string>();

        var result = FileReadHelper.AddLineNumbers(lines, 1, "\n");

        result.Should().BeEmpty();
    }

    [Test]
    public void AddLineNumbers_SingleLine()
    {
        var lines = new[] { "only line" };

        var result = FileReadHelper.AddLineNumbers(lines, 1, "\n");

        result.Should().Be("1: only line");
    }

    [Test]
    public void CountLines_SingleLine()
    {
        var result = FileReadHelper.CountLines("hello");

        result.Should().Be(1);
    }

    [Test]
    public void CountLines_MultipleLines()
    {
        var result = FileReadHelper.CountLines("line1\nline2\nline3");

        result.Should().Be(3);
    }

    [Test]
    public void CountLines_EmptyString()
    {
        var result = FileReadHelper.CountLines("");

        result.Should().Be(0);
    }

    [Test]
    public void CountLines_TrailingNewlineDoesNotAddPhantomLine()
    {
        // Canonical (ReadAllLines) semantics: "line1\nline2\n" is 2 lines, not 3.
        var result = FileReadHelper.CountLines("line1\nline2\n");

        result.Should().Be(2);
    }

    [Test]
    public void CountLines_CrLf_IsCountedAsOneLineEnding()
    {
        var result = FileReadHelper.CountLines("line1\r\nline2\r\nline3\r\n");

        result.Should().Be(3);
    }
}
