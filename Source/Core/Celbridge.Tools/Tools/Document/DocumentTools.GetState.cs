using System.Text.Json;
using Celbridge.Documents;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by document_get_state with the visual state of the document editor.
/// </summary>
public record class DocumentStateResult(
    string ActiveDocument,
    int SectionCount,
    List<OpenDocumentEntry> OpenDocuments);

/// <summary>
/// An open document entry within the document_get_state result.
/// </summary>
public record class OpenDocumentEntry(string Resource, int SectionIndex, int TabOrder, bool IsActive, string EditorId);

public partial class DocumentTools
{
    /// <summary>Document editor state: active document, section layout, all open tabs and their positions.</summary>
    [McpServerTool(Name = "document_get_state", ReadOnly = true)]
    [ToolAlias("document.get_state")]
    public async partial Task<CallToolResult> GetState()
    {
        const string ToolGuide = "document_get_state";

        // Route through the command queue so the snapshot observes state after all
        // previously enqueued commands have run. The underlying read is served from
        // a cache on DocumentsService, so this never touches WinUI collections.
        var getStateResult = await ExecuteCommandAsync<IGetDocumentStateCommand, DocumentStateSnapshot>();
        if (getStateResult.IsFailure)
        {
            return ToolResponse.Error(getStateResult, ToolGuide);
        }
        var snapshot = getStateResult.Value;

        var activeDocument = snapshot.ActiveDocument;

        var documents = new List<OpenDocumentEntry>();
        foreach (var document in snapshot.OpenDocuments)
        {
            documents.Add(new OpenDocumentEntry(
                document.FileResource.ToString(),
                document.Address.SectionIndex,
                document.Address.TabOrder,
                document.FileResource == activeDocument,
                document.EditorId.ToString()));
        }

        var result = new DocumentStateResult(
            activeDocument.ToString(),
            snapshot.SectionCount,
            documents);

        var json = JsonSerializer.Serialize(result, JsonOptions);
        return ToolResponse.Success(json);
    }
}
