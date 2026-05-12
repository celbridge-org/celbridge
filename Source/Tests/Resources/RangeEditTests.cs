using Celbridge.Resources;

namespace Celbridge.Tests.Resources;

[TestFixture]
public class RangeEditTests
{
    [Test]
    public void Insert_CreatesZeroWidthRange()
    {
        var edit = RangeEdit.Insert(line: 5, column: 10, text: "inserted");

        edit.Line.Should().Be(5);
        edit.Column.Should().Be(10);
        edit.EndLine.Should().Be(5);
        edit.EndColumn.Should().Be(10);
        edit.NewText.Should().Be("inserted");
    }

    [Test]
    public void Delete_CreatesRangeWithEmptyText()
    {
        var edit = RangeEdit.Delete(line: 1, column: 1, endLine: 1, endColumn: 5);

        edit.Line.Should().Be(1);
        edit.Column.Should().Be(1);
        edit.EndLine.Should().Be(1);
        edit.EndColumn.Should().Be(5);
        edit.NewText.Should().BeEmpty();
    }

    [Test]
    public void Replace_CreatesRangeWithNewText()
    {
        var edit = RangeEdit.Replace(line: 2, column: 3, endLine: 4, endColumn: 8, newText: "replacement");

        edit.Line.Should().Be(2);
        edit.Column.Should().Be(3);
        edit.EndLine.Should().Be(4);
        edit.EndColumn.Should().Be(8);
        edit.NewText.Should().Be("replacement");
    }

    [Test]
    public void Constructor_CreatesEditDirectly()
    {
        var edit = new RangeEdit(Line: 1, Column: 1, EndLine: 2, EndColumn: 5, NewText: "text");

        edit.Line.Should().Be(1);
        edit.Column.Should().Be(1);
        edit.EndLine.Should().Be(2);
        edit.EndColumn.Should().Be(5);
        edit.NewText.Should().Be("text");
    }

    [Test]
    public void FileRangeEdit_ContainsResourceAndEdits()
    {
        var resource = new ResourceKey("test/file.txt");
        var edits = new List<RangeEdit>
        {
            RangeEdit.Insert(1, 1, "first"),
            RangeEdit.Replace(2, 1, 2, 10, "second")
        };

        var fileEdit = new FileRangeEdit(resource, edits);

        fileEdit.Resource.Should().Be(resource);
        fileEdit.Edits.Should().HaveCount(2);
    }

    [Test]
    public void Insert_AtEndOfLine_UsesColumnAfterLastCharacter()
    {
        // To append to a line with 20 characters, insert at column 21
        var edit = RangeEdit.Insert(line: 3, column: 21, text: " appended");

        edit.Line.Should().Be(3);
        edit.Column.Should().Be(21);
        edit.EndLine.Should().Be(edit.Line);
        edit.EndColumn.Should().Be(edit.Column);
    }

    [Test]
    public void Delete_MultipleLines_SpansLineRange()
    {
        // Delete from line 5 column 1 to line 8 column 1 (deletes lines 5-7 entirely)
        var edit = RangeEdit.Delete(line: 5, column: 1, endLine: 8, endColumn: 1);

        edit.Line.Should().Be(5);
        edit.EndLine.Should().Be(8);
        edit.NewText.Should().BeEmpty();
    }
}
