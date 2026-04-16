namespace Celbridge.Documents;

/// <summary>
/// Describes the result of a successful call to IDocumentsService.OpenDocument.
/// A Result.Fail is still reserved for genuine errors (file not found, factory failure, etc.).
/// This enum distinguishes the valid outcomes within a Result.Ok so that callers can tell
/// a successful open from a user-initiated cancellation and react appropriately (e.g. suppress
/// error dialogs when the user deliberately cancelled).
/// </summary>
public enum OpenDocumentOutcome
{
    /// <summary>
    /// The document is now open and ready, either because a new tab was created or an
    /// existing tab for the same resource was activated.
    /// </summary>
    Opened,

    /// <summary>
    /// The open operation was cancelled. This happens when opening a document would require
    /// closing an existing tab (for example, when "Open With..." switches an already-open file
    /// to a different editor) and the close was refused. The refusal can originate from the user
    /// (declining a save-prompt dialog) or programmatically from the document view itself
    /// (a mod-defined editor returning false from CanClose, e.g. a permanent dashboard panel).
    /// The document state is unchanged and no error should be surfaced to the user.
    /// </summary>
    Cancelled,
}
