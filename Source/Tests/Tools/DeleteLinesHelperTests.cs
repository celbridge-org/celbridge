using Celbridge.Resources;
using Celbridge.Resources.Commands;

namespace Celbridge.Tests.Tools;

/// <summary>
/// Tests for DeleteLinesHelper — the pure logic helpers for the
/// file_delete_lines MCP tool.
/// </summary>
[TestFixture]
public class DeleteLinesHelperTests
{
    [Test]
    public void CreateDeleteEdit_MiddleLines_SpansToNextLineStart()
    {
        // Deleting lines 3-5 from a 10-line file should create a range
        // from (3, 1) to (6, 1) to consume the line terminators
        var edit = DeleteLinesHelper.CreateDeleteEdit(3, 5, 10);

        edit.Line.Should().Be(3);
        edit.Column.Should().Be(1);
        edit.EndLine.Should().Be(6);
        edit.EndColumn.Should().Be(1);
        edit.NewText.Should().BeEmpty();
    }

    [Test]
    public void CreateDeleteEdit_LastLines_SpansToEndOfLastLine()
    {
        // Deleting lines 8-10 from a 10-line file should create a range
        // from (8, 1) to (10, MaxValue) since there's no next line
        var edit = DeleteLinesHelper.CreateDeleteEdit(8, 10, 10);

        edit.Line.Should().Be(8);
        edit.Column.Should().Be(1);
        edit.EndLine.Should().Be(10);
        edit.EndColumn.Should().Be(int.MaxValue);
        edit.NewText.Should().BeEmpty();
    }

    [Test]
    public void CreateDeleteEdit_SingleMiddleLine_SpansToNextLineStart()
    {
        var edit = DeleteLinesHelper.CreateDeleteEdit(5, 5, 10);

        edit.Line.Should().Be(5);
        edit.Column.Should().Be(1);
        edit.EndLine.Should().Be(6);
        edit.EndColumn.Should().Be(1);
        edit.NewText.Should().BeEmpty();
    }

    [Test]
    public void CreateDeleteEdit_SingleLastLine_SpansToEndOfLine()
    {
        var edit = DeleteLinesHelper.CreateDeleteEdit(10, 10, 10);

        edit.Line.Should().Be(10);
        edit.Column.Should().Be(1);
        edit.EndLine.Should().Be(10);
        edit.EndColumn.Should().Be(int.MaxValue);
        edit.NewText.Should().BeEmpty();
    }

    [Test]
    public void CreateDeleteEdit_FirstLines_SpansToNextLineStart()
    {
        var edit = DeleteLinesHelper.CreateDeleteEdit(1, 3, 10);

        edit.Line.Should().Be(1);
        edit.Column.Should().Be(1);
        edit.EndLine.Should().Be(4);
        edit.EndColumn.Should().Be(1);
        edit.NewText.Should().BeEmpty();
    }

    [Test]
    public void DeleteLinesFromList_RemovesMiddleLines()
    {
        var lines = new List<string> { "line1", "line2", "line3", "line4", "line5" };

        var result = DeleteLinesHelper.DeleteLinesFromList(lines, 2, 4);

        result.IsSuccess.Should().BeTrue();
        lines.Should().BeEquivalentTo(new[] { "line1", "line5" });
    }

    [Test]
    public void DeleteLinesFromList_RemovesFirstLines()
    {
        var lines = new List<string> { "line1", "line2", "line3", "line4" };

        var result = DeleteLinesHelper.DeleteLinesFromList(lines, 1, 2);

        result.IsSuccess.Should().BeTrue();
        lines.Should().BeEquivalentTo(new[] { "line3", "line4" });
    }

    [Test]
    public void DeleteLinesFromList_RemovesLastLines()
    {
        var lines = new List<string> { "line1", "line2", "line3", "line4" };

        var result = DeleteLinesHelper.DeleteLinesFromList(lines, 3, 4);

        result.IsSuccess.Should().BeTrue();
        lines.Should().BeEquivalentTo(new[] { "line1", "line2" });
    }

    [Test]
    public void DeleteLinesFromList_RemovesSingleLine()
    {
        var lines = new List<string> { "line1", "line2", "line3" };

        var result = DeleteLinesHelper.DeleteLinesFromList(lines, 2, 2);

        result.IsSuccess.Should().BeTrue();
        lines.Should().BeEquivalentTo(new[] { "line1", "line3" });
    }

    [Test]
    public void DeleteLinesFromList_RemovesAllLines()
    {
        var lines = new List<string> { "line1", "line2", "line3" };

        var result = DeleteLinesHelper.DeleteLinesFromList(lines, 1, 3);

        result.IsSuccess.Should().BeTrue();
        lines.Should().BeEmpty();
    }

    [Test]
    public void DeleteLinesFromList_StartLineOutOfRange_Fails()
    {
        var lines = new List<string> { "line1", "line2" };

        var result = DeleteLinesHelper.DeleteLinesFromList(lines, 5, 5);

        result.IsFailure.Should().BeTrue();
        lines.Should().HaveCount(2);
    }

    [Test]
    public void DeleteLinesFromList_EndLineOutOfRange_Fails()
    {
        var lines = new List<string> { "line1", "line2" };

        var result = DeleteLinesHelper.DeleteLinesFromList(lines, 1, 5);

        result.IsFailure.Should().BeTrue();
        lines.Should().HaveCount(2);
    }

    [Test]
    public void DeleteLinesFromList_ZeroStartLine_Fails()
    {
        var lines = new List<string> { "line1", "line2" };

        var result = DeleteLinesHelper.DeleteLinesFromList(lines, 0, 1);

        result.IsFailure.Should().BeTrue();
        lines.Should().HaveCount(2);
    }
}
