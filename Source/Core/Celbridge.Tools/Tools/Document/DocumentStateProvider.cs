using Celbridge.Commands;
using Celbridge.Documents;

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
public record class OpenDocumentEntry(
    string Resource,
    int SectionIndex,
    int TabOrder,
    bool IsActive,
    string EditorId);

/// <summary>
/// Builds the DocumentStateResult snapshot consumed by both the
/// document_get_state tool and AgentResponseFilter's session-start
/// auto-attach. The snapshot routes through the command queue so it
/// observes state after all previously enqueued commands have run.
/// </summary>
public interface IDocumentStateProvider
{
    Task<Result<DocumentStateResult>> GetStateAsync();
}

internal sealed class DocumentStateProvider : IDocumentStateProvider
{
    private readonly ICommandService _commandService;

    public DocumentStateProvider(ICommandService commandService)
    {
        _commandService = commandService;
    }

    public async Task<Result<DocumentStateResult>> GetStateAsync()
    {
        var snapshotResult = await _commandService.ExecuteAsync<IGetDocumentStateCommand, DocumentStateSnapshot>();
        if (snapshotResult.IsFailure)
        {
            return Result.Fail(snapshotResult);
        }
        var snapshot = snapshotResult.Value;

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

        // An empty active document key (no document open) serialises as the
        // empty string rather than the canonical "project:" form, so the
        // response field is a clean signal that nothing is active.
        var activeDocumentString = activeDocument.IsEmpty ? string.Empty : activeDocument.ToString();

        return new DocumentStateResult(
            activeDocumentString,
            snapshot.SectionCount,
            documents);
    }
}
