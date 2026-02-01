namespace Celbridge.Documents;

/// <summary>
/// A message that indicates the current number of pending document saves.
/// </summary>
public record PendingDocumentSaveMessage(int PendingSaveCount);

/// <summary>
/// A notification that the document layout has changed (documents opened, closed, or moved).
/// Receivers should query IDocumentsService for current state if needed.
/// </summary>
public record DocumentLayoutChangedMessage();

/// <summary>
/// A message sent when the selected document changes.
/// </summary>
public record SelectedDocumentChangedMessage(ResourceKey DocumentResource);

/// <summary>
/// A message sent when a document tab is clicked/tapped.
/// Used to update the active document.
/// </summary>
public record DocumentTabClickedMessage(ResourceKey DocumentResource, DocumentAddress Address);

/// <summary>
/// A message sent when the document section proportions change.
/// Contains ratios (relative values that sum to 1.0).
/// </summary>
public record SectionRatiosChangedMessage(List<double> SectionRatios);

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
