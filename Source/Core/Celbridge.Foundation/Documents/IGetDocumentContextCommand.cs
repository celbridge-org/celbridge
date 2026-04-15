using Celbridge.Commands;

namespace Celbridge.Documents;

/// <summary>
/// Snapshot of the documents panel state produced by IGetDocumentContextCommand.
/// </summary>
public record class DocumentContextSnapshot(
    ResourceKey ActiveDocument,
    int SectionCount,
    IReadOnlyList<OpenDocumentInfo> OpenDocuments);

/// <summary>
/// Read-only query that captures the current documents panel state (active document,
/// section count, open documents) in a snapshot. Routed through the command queue so
/// callers observe state that is consistent with all previously enqueued commands.
/// </summary>
public interface IGetDocumentContextCommand : IExecutableCommand<DocumentContextSnapshot>
{
}
