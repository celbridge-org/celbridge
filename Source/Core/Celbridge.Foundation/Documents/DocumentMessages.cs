using Celbridge.Reports;

namespace Celbridge.Documents;

/// <summary>
/// A notification that the document layout has changed (documents opened, closed, or moved).
/// Receivers should query IDocumentsService for current state if needed.
/// </summary>
public record DocumentLayoutChangedMessage();

/// <summary>
/// A message sent when the active document changes.
/// </summary>
public record ActiveDocumentChangedMessage(ResourceKey DocumentResource);

/// <summary>
/// A message sent when an area's split state or split position changes. The ratio is the share of the
/// area taken by its primary section, and is meaningful only while the area is split.
/// </summary>
public record AreaLayoutChangedMessage(DocumentArea Area, bool IsSplit, double SplitRatio);

/// <summary>
/// A message sent when an open document's resource has been renamed or moved.
/// </summary>
public record DocumentResourceChangedMessage(ResourceKey OldResource, ResourceKey NewResource);

/// <summary>
/// A message sent when a document save operation has completed.
/// Automatically sent by DocumentView.SaveAsync() after a successful save.
/// </summary>
public record DocumentSaveCompletedMessage(ResourceKey DocumentResource);

/// <summary>
/// A message sent when a document view's content area receives focus.
/// </summary>
public record DocumentViewFocusedMessage(ResourceKey DocumentResource);

/// <summary>
/// A message sent when a document has been opened and its view created. Paired with
/// DocumentClosedMessage: both are sent by DocumentTabViewModel, so a listener can own a resource for
/// exactly as long as its document is open. Docked utilities are presented rather than opened, and take
/// no part in the pairing.
/// </summary>
public record DocumentOpenedMessage(ResourceKey DocumentResource);

/// <summary>
/// A message sent when a document has been closed and its tab removed. The counterpart to
/// DocumentOpenedMessage; see its summary for the pairing. Sent on every close path, including a reopen
/// that swaps to a different editor, which removes the tab without going through the documents service.
/// </summary>
public record DocumentClosedMessage(ResourceKey DocumentResource);

/// <summary>
/// A message sent when an editor running in a WebView asks the host to tell the user something. The
/// message text is one line, resolved by the editor rather than by the host, following the same
/// producer-side rule report content does.
/// </summary>
public record EditorNotificationMessage(
    ReportSeverity Severity,
    string Message)
{
    /// <summary>
    /// The document the notification's action opens, or null when the editor offered none.
    /// </summary>
    public OpenDocumentAction? Action { get; init; }
}
