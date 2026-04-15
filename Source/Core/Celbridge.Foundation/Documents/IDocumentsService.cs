namespace Celbridge.Documents;

/// <summary>
/// The documents service provides functionality to support the documents panel in the workspace UI.
/// </summary>
public interface IDocumentsService
{
    /// <summary>
    /// The registry for document editor factories.
    /// </summary>
    IDocumentEditorRegistry DocumentEditorRegistry { get; }

    /// <summary>
    /// Restores the state of the documents panel from the previous session.
    /// </summary>
    Task RestorePanelState();

    /// <summary>
    /// The resource key for the currently active document.
    /// This is the empty resource if no document is currently active.
    /// </summary>
    ResourceKey ActiveDocument { get; }

    /// <summary>
    /// The number of visible document sections in the documents panel.
    /// This is a cached snapshot that is safe to read from any thread.
    /// </summary>
    int SectionCount { get; }

    /// <summary>
    /// Returns a snapshot of all open documents with their addresses and editor IDs.
    /// This is a cached snapshot that is safe to read from any thread.
    /// </summary>
    IReadOnlyList<OpenDocumentInfo> GetOpenDocuments();

    /// <summary>
    /// Create a document view for the specified file resource.
    /// The type of document view created is based on the file extension.
    /// When documentEditorId is specified, uses that specific editor instead of the default.
    /// Fails if the file resource does not exist.
    /// </summary>
    Task<Result<IDocumentView>> CreateDocumentView(ResourceKey fileResource, DocumentEditorId editorId = default);

    /// <summary>
    /// Returns the document view type for the specified file resource.
    /// </summary>
    DocumentViewType GetDocumentViewType(ResourceKey fileResource);

    /// <summary>
    /// Determines if a file resource can be opened as a document in the editor.
    /// Returns false if the file format is not supported or the resource is not a file.
    /// </summary>
    bool IsDocumentSupported(ResourceKey fileResource);

    /// <summary>
    /// Returns the text editor language associated with the specified file resource.
    /// Returns an empty string if no matching language is found.
    /// </summary>
    string GetDocumentLanguage(ResourceKey fileResource);

    /// <summary>
    /// Opens a file resource as a document in the documents panel.
    /// </summary>
    Task<Result> OpenDocument(ResourceKey fileResource, OpenDocumentOptions? options = null);

    /// <summary>
    /// Closes an opened document in the documents panel.
    /// forceClose forces the document to close without allowing the document to cancel the close operation.
    /// </summary>
    Task<Result> CloseDocument(ResourceKey fileResource, bool forceClose);

    /// <summary>
    /// Activates an opened document in the documents panel, making it the active tab.
    /// Fails if the specified document is not opened.
    /// </summary>
    Result ActivateDocument(ResourceKey fileResource);

    /// <summary>
    /// Save any modified documents to disk.
    /// This method is called on a timer to save modified documents at regular intervals.
    /// Delta time is the time since this method was last called.
    /// </summary>
    Task<Result> SaveModifiedDocuments(double deltaTime);

    /// <summary>
    /// Stores the current document layout (open documents and their addresses) in persistent storage.
    /// This layout will be restored at the start of the next editing session.
    /// </summary>
    Task StoreDocumentLayout();

    /// <summary>
    /// Stores the currently active document in persistent storage.
    /// This document will be activated at the start of the next editing session.
    /// </summary>
    Task StoreActiveDocument();

    /// <summary>
    /// Saves editor UI state (scroll position, view mode, etc.) for all open documents.
    /// </summary>
    Task StoreEditorStates();

    /// <summary>
    /// Clears the stored editor state for a specific document.
    /// Called when a document fails to open, to prevent stale state from persisting.
    /// </summary>
    Task ClearEditorState(ResourceKey fileResource);
}
