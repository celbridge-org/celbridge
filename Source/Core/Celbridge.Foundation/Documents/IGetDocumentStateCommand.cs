using Celbridge.Commands;

namespace Celbridge.Documents;

/// <summary>
/// Snapshot of the documents panel state produced by IGetDocumentStateCommand.
/// </summary>
public record class DocumentStateSnapshot(
    IReadOnlyList<DocumentSection> VisibleSections,
    IReadOnlyList<OpenDocumentInfo> OpenDocuments,
    IReadOnlyDictionary<DocumentSection, ResourceKey> SelectedDocuments,
    ResourceKey ActiveDocument);

/// <summary>
/// Read-only query that captures the current documents panel state in a snapshot. Routed through the
/// command queue so callers observe state that is consistent with all previously enqueued commands.
/// </summary>
public interface IGetDocumentStateCommand : IExecutableCommand<DocumentStateSnapshot>
{
}
