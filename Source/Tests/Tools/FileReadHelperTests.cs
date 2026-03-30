using Celbridge.Tools;

namespace Celbridge.Tests.Tools;

/// <summary>
/// Tests for FileReadHelper — line numbering and line counting helpers
/// used by the file_read MCP tool.
/// </summary>
[TestFixture]
public class FileReadHelperTests
{
    [Test]
    public void AddLineNumbers_PrefixesLinesFromOne()
    {
        var lines = new[] { "# Heading", "", "Some text" };

        var result = FileReadHelper.AddLineNumbers(lines, 1);

        var expected = "1: # Heading" + Environment.NewLine +
                       "2: " + Environment.NewLine +
                       "3: Some text";
        result.Should().Be(expected);
    }

    [Test]
    public void AddLineNumbers_PrefixesLinesFromOffset()
    {
        var lines = new[] { "line five", "line six", "line seven" };

        var result = FileReadHelper.AddLineNumbers(lines, 5);

        var expected = "5: line five" + Environment.NewLine +
                       "6: line six" + Environment.NewLine +
                       "7: line seven";
        result.Should().Be(expected);
    }

    [Test]
    public void AddLineNumbers_EmptyArray_ReturnsEmptyString()
    {
        var lines = Array.Empty<string>();

        var result = FileReadHelper.AddLineNumbers(lines, 1);

        result.Should().BeEmpty();
    }

    [Test]
    public void AddLineNumbers_SingleLine()
    {
        var lines = new[] { "only line" };

        var result = FileReadHelper.AddLineNumbers(lines, 1);

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
    public void CountLines_TrailingNewline()
    {
        var result = FileReadHelper.CountLines("line1\nline2\n");

        result.Should().Be(3);
    }
}
