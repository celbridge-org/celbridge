namespace Celbridge.Documents;

/// <summary>
/// A message that indicates the current number of pending document saves.
/// </summary>
public record PendingDocumentSaveMessage(int PendingSaveCount);
