using Celbridge.Commands;

namespace Celbridge.Documents;

/// <summary>
/// Snapshot of the documents panel state produced by IGetDocumentStateCommand.
/// </summary>
public record class DocumentStateSnapshot(
    ResourceKey ActiveDocument,
    IReadOnlyList<DocumentSection> VisibleSections,
    IReadOnlyList<OpenDocumentInfo> OpenDocuments);

/// <summary>
/// Read-only query that captures the current documents panel state (active document,
/// visible sections, open documents) in a snapshot. Routed through the command queue so
/// callers observe state that is consistent with all previously enqueued commands.
/// </summary>
public interface IGetDocumentStateCommand : IExecutableCommand<DocumentStateSnapshot>
{
}
