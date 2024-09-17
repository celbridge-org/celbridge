namespace Celbridge.Documents;

/// <summary>
/// The documents service provides functionality to support the documents panel in the workspace UI.
/// </summary>
public interface IDocumentsService
{
    /// <summary>
    /// The documents panel created via the CreateDocumentsPanel method.
    /// </summary>
    IDocumentsPanel? DocumentsPanel { get; }

    /// <summary>
    /// The resource key for the currently selected document.
    /// This is the empty resource if no document is currently selected.
    /// </summary>
    ResourceKey SelectedDocument { get; }

    /// <summary>
    /// The list of currently open documents.
    /// </summary>
    List<ResourceKey> OpenDocuments { get; }

    /// <summary>
    /// Factory method to create the documents panel for the workspace UI.
    /// </summary>
    IDocumentsPanel CreateDocumentsPanel();

    /// <summary>
    /// Factory method to create a document view for the specified view type.
    /// </summary>
    Task<Result<IDocumentView>> CreateDocumentView(ResourceKey fileResource);

    /// <summary>
    /// Opens a file resource as a document in the documents panel.
    /// </summary>
    Task<Result> OpenDocument(ResourceKey fileResource);

    /// <summary>
    /// Closes an opened document in the documents panel.
    /// forceClose forces the document to close without allowing the document to cancel the close operation.
    /// </summary>
    Task<Result> CloseDocument(ResourceKey fileResource, bool forceClose);

    /// <summary>
    /// Selects an opened document in the documents panel.
    /// Fails if the specified document is not opened.
    /// </summary>
    Result SelectDocument(ResourceKey fileResource);

    /// <summary>
    /// Save any modified documents to disk.
    /// This method is called on a timer to save modified documents at regular intervals.
    /// Delta time is the time since this method was last called.
    /// </summary>
    Task<Result> SaveModifiedDocuments(double deltaTime);

    /// <summary>
    /// Stores the list of currently open documents in persistent storage.
    /// These documents will be opened at the start of the next editing session.
    /// </summary>
    Task StoreOpenDocuments();

    /// <summary>
    /// Stores the currently selected document in persistent storage.
    /// This document will be selected at the start of the next editing session.
    /// </summary>
    Task StoreSelectedDocument();

    /// <summary>
    /// Restores the state of the panel from the previous session.
    /// </summary>
    Task RestorePanelState();

    /// <summary>
    /// Returns the language associated with the specified file extension.
    /// Returns an empty string if no matching language is found.
    /// </summary>
    string GetDocumentLanguage(string fileExtension);
}
