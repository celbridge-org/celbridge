namespace Celbridge.Documents;

/// <summary>
/// A message that indicates the current number of pending document saves.
/// </summary>
public record PendingDocumentSaveMessage(int PendingSaveCount);

/// <summary>
/// A message sent when the list of opened documents changes.
/// </summary>
public record OpenDocumentsChangedMessage(List<ResourceKey> OpenDocuments);

/// <summary>
/// A message sent when the selected document changes.
/// </summary>
public record SelectedDocumentChangedMessage(ResourceKey DocumentResource);

/// <summary>
/// A message sent when an open document's resource has been renamed or moved.
/// </summary>
public record DocumentResourceChangedMessage(ResourceKey OldResource, ResourceKey NewResource);

/// <summary>
/// A message sent when a previously modified document has been requested to save to disk.
/// </summary>
public record DocumentSaveRequestedMessage(ResourceKey DocumentResource);

/// <summary>
/// A message sent when a document save operation has completed.
/// This message is only sent for specific document types with complex async save sequences (e.g. spreadsheets).
/// </summary>
public record DocumentSaveCompletedMessage(ResourceKey DocumentResource);
