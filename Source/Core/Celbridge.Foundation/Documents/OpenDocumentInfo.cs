namespace Celbridge.Documents;

/// <summary>
/// Snapshot of an open document's state, used for persistence and querying.
/// </summary>
public record OpenDocumentInfo(ResourceKey FileResource, DocumentAddress Address, DocumentEditorId EditorId);
