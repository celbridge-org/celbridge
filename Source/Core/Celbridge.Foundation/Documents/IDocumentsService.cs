namespace Celbridge.Documents;

/// <summary>
/// How an upcoming external reload should treat the editor's current view state.
/// View state is editor-specific — concrete examples include scroll position, cursor
/// or selection, zoom level, and fold state — and the editor decides what to capture
/// and restore.
/// </summary>
public enum ReloadHint
{
    /// <summary>
    /// Default. The editor keeps its current view state across the reload, regardless
    /// of any view state encoded in the on-disk file.
    /// </summary>
    PreserveViewState,

    /// <summary>
    /// The editor adopts the view state encoded in the on-disk file, or its default
    /// view state when the file format does not persist view state. Used by commands
    /// whose purpose is to update the document's persisted view.
    /// </summary>
    DiskWinsOnViewState
}

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
    /// Creates a document view for the given file resource. When editorId is
    /// non-empty, uses that specific editor instead of the default resolution
    /// chain. Fails if the resource does not exist.
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
    /// Returns the stored editor preference for a file extension, or DocumentEditorId.Empty
    /// if no preference is stored (or the stored value is invalid).
    /// </summary>
    Task<DocumentEditorId> GetEditorPreferenceAsync(string extension);

    /// <summary>
    /// Returns the editor that would open this file: the sidecar's 'editor'
    /// field when set, otherwise the per-extension preference, otherwise
    /// DocumentEditorId.Empty.
    /// </summary>
    Task<DocumentEditorId> GetPreferredEditorAsync(ResourceKey fileResource);

    /// <summary>
    /// Stores the user's preferred editor for a file extension. Pass DocumentEditorId.Empty
    /// to clear the preference.
    /// </summary>
    Task SetEditorPreferenceAsync(string extension, DocumentEditorId editorId);

    /// <summary>
    /// Opens a file resource as a document in the documents panel.
    /// </summary>
    Task<Result<OpenDocumentOutcome>> OpenDocument(ResourceKey fileResource, OpenDocumentOptions? options = null);

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
    /// Saves editor state (scroll position, view mode, etc.) for all open documents.
    /// </summary>
    Task StoreDocumentEditorStates();

    /// <summary>
    /// Saves editor state for a single document. Called when a document tab is about to close
    /// so its state survives close/reopen within the same workspace session. Pass a non-empty state
    /// string to persist, or null/empty to clear any existing entry for the resource.
    /// </summary>
    Task StoreDocumentEditorState(ResourceKey fileResource, string? state);

    /// <summary>
    /// Records a hint that the next watcher-driven reload of the resource should
    /// honour. Overwrites any prior hint for the same resource; hints expire if
    /// not consumed within a short window so they do not bleed into later
    /// unrelated reloads.
    /// </summary>
    void RegisterReloadHint(ResourceKey fileResource, ReloadHint hint);

    /// <summary>
    /// Returns the most recently registered hint for the resource and removes it
    /// from the store. Returns PreserveViewState when no fresh hint is set.
    /// </summary>
    ReloadHint ConsumeReloadHint(ResourceKey fileResource);
}
