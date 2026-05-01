using System.Text.Json;
using Celbridge.Documents;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

/// <summary>
/// Result returned by document_get_context with the visual state of the document editor.
/// </summary>
public record class DocumentContextResult(
    string ActiveDocument,
    int SectionCount,
    List<OpenDocumentEntry> OpenDocuments);

/// <summary>
/// An open document entry within the document_get_context result.
/// </summary>
public record class OpenDocumentEntry(string Resource, int SectionIndex, int TabOrder, bool IsActive, string EditorId);

public partial class DocumentTools
{
    /// <summary>
    /// Gets the visual state of the document editor: the active document, the editor
    /// section layout, and all open documents with their positions. Use this to
    /// understand what the user is currently looking at in the editor.
    /// </summary>
    /// <returns>JSON object with fields: activeDocument (string, resource key of the active document or empty), sectionCount (int, number of visible editor sections 1-3), openDocuments (array of objects with resource (string), sectionIndex (int), tabOrder (int), isActive (bool), editorId (string, e.g. "celbridge.code-editor"; empty when no editor is bound)).</returns>
    [McpServerTool(Name = "document_get_context", ReadOnly = true)]
    [ToolAlias("document.get_context")]
    public async partial Task<CallToolResult> GetContext()
    {
        // Route through the command queue so the snapshot observes state after all
        // previously enqueued commands have run. The underlying read is served from
        // a cache on DocumentsService, so this never touches WinUI collections.
        var (callResult, snapshot) = await ExecuteCommandAsync<IGetDocumentContextCommand, DocumentContextSnapshot>();
        if (callResult.IsError == true || snapshot is null)
        {
            return callResult;
        }

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

        var result = new DocumentContextResult(
            activeDocument.ToString(),
            snapshot.SectionCount,
            documents);

        var json = JsonSerializer.Serialize(result, JsonOptions);
        return SuccessResult(json);
    }
}
