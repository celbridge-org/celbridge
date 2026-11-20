using System.Text.Json.Nodes;
using Celbridge.LanguageServers.Helpers;
using Celbridge.Resources;

namespace Celbridge.Tests.LanguageServers;

/// <summary>
/// Covers reading a server's workspace edit as range edits for each document.
/// </summary>
[TestFixture]
public class WorkspaceEditReaderTests
{
    private const string MainUri = "file:///Projects/Acme/src/main.ts";
    private const string OtherUri = "file:///Projects/Acme/src/other.ts";

    private const string TextEdit = """{"range":{"start":{"line":3,"character":4},"end":{"line":3,"character":10}},"newText":"size"}""";

    [Test]
    public void Read_Changes_ReturnsOneDocumentForEachFileWithoutAVersion()
    {
        var edit = JsonNode.Parse($$$"""{"changes":{"{{{MainUri}}}":[{{{TextEdit}}}],"{{{OtherUri}}}":[{{{TextEdit}}}]}}""");

        var result = WorkspaceEditReader.Read(edit);

        result.IsSuccess.Should().BeTrue();
        var documentEdits = result.Value;
        documentEdits.Select(documentEdit => documentEdit.Uri).Should().Equal(MainUri, OtherUri);
        documentEdits[0].Version.Should().BeNull();
        documentEdits[0].Edits.Should().Equal(new RangeEdit(4, 5, 4, 11, "size"));
    }

    [Test]
    public void Read_DocumentChanges_ReturnsEachDocumentsVersion_AndWinsOverChanges()
    {
        var edit = JsonNode.Parse($$"""
            {
              "changes": {"{{OtherUri}}": [{{TextEdit}}]},
              "documentChanges": [{"textDocument": {"uri": "{{MainUri}}", "version": 7}, "edits": [{{TextEdit}}]}]
            }
            """);

        var result = WorkspaceEditReader.Read(edit);

        result.IsSuccess.Should().BeTrue();
        var documentEdit = result.Value.Single();
        documentEdit.Uri.Should().Be(MainUri);
        documentEdit.Version.Should().Be(7);
    }

    [Test]
    public void Read_AnOperationOnAFile_IsRefused()
    {
        var edit = JsonNode.Parse($$$"""{"documentChanges":[{"kind":"create","uri":"{{{OtherUri}}}"}]}""");

        var result = WorkspaceEditReader.Read(edit);

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("creates, renames or deletes");
    }

    [Test]
    public void Read_ATextEditWithoutNewText_IsRefused()
    {
        var edit = JsonNode.Parse($$$$"""{"changes":{"{{{{MainUri}}}}":[{"range":{"start":{"line":0,"character":0},"end":{"line":0,"character":0}}}]}}""");

        var result = WorkspaceEditReader.Read(edit);

        result.IsFailure.Should().BeTrue();
    }
}
