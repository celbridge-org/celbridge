using Celbridge.Explorer;

namespace Celbridge.Documents;

/// <summary>
/// A message that indicates the current number of pending document saves.
/// </summary>
public record PendingDocumentSaveMessage(int PendingSaveCount);

/// <summary>
/// A message sent when the selected document in the document panel changes.
/// </summary>
public record SelectedDocumentChangedMessage(ResourceKey DocumentResource);

