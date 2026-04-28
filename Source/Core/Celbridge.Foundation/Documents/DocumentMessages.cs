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
/// A message sent when the active document changes.
/// </summary>
public record ActiveDocumentChangedMessage(ResourceKey DocumentResource);

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
/// A message sent when a document save operation has completed.
/// Automatically sent by DocumentView.SaveDocument() after a successful save.
/// </summary>
public record DocumentSaveCompletedMessage(ResourceKey DocumentResource);

/// <summary>
/// A message sent when a document view's content area receives focus.
/// </summary>
public record DocumentViewFocusedMessage(ResourceKey DocumentResource);

/// <summary>
/// A message sent to request a web view document to navigate to a URL.
/// </summary>
public record WebViewNavigateMessage(ResourceKey DocumentResource, string Url);

/// <summary>
/// A message sent to request a web view document to refresh with cache clearing.
/// </summary>
public record WebViewRefreshMessage(ResourceKey DocumentResource);

/// <summary>
/// A message sent to request a web view document to navigate back in history.
/// </summary>
public record WebViewGoBackMessage(ResourceKey DocumentResource);

/// <summary>
/// A message sent to request a web view document to navigate forward in history.
/// </summary>
public record WebViewGoForwardMessage(ResourceKey DocumentResource);

/// <summary>
/// A message sent when the web view document's navigation state changes.
/// </summary>
public record WebViewNavigationStateChangedMessage(ResourceKey DocumentResource, bool CanGoBack, bool CanGoForward, bool CanRefresh, string CurrentUrl);
