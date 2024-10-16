using Celbridge.ExtensionAPI;

namespace Celbridge.Documents;

/// <summary>
/// The documents service provides functionality to support the documents panel in the workspace UI.
/// </summary>
public interface IDocumentsService
{
    /// <summary>
    /// Returns the documents panel view.
    /// </summary>
    IDocumentsPanel DocumentsPanel { get; }

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
    /// Create a document view for the specified file resource.
    /// The type of document view created is based on the file extension.
    /// Fails if the file resource does not exist.
    /// </summary>
    Task<Result<IDocumentView>> CreateDocumentView(ResourceKey fileResource);

    /// <summary>
    /// Returns the document view type for the specified file resource.
    /// </summary>
    DocumentViewType GetDocumentViewType(ResourceKey fileResource);

    /// <summary>
    /// Returns the text editor language associated with the specified file resource.
    /// Returns an empty string if no matching language is found.
    /// </summary>
    string GetDocumentLanguage(ResourceKey fileResource);

    /// <summary>
    /// Opens a file resource as a document in the documents panel.
    /// </summary>
    Task<Result> OpenDocument(ResourceKey fileResource, bool forceReload);

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
    /// Restores the state of the documents panel from the previous session.
    /// </summary>
    Task RestorePanelState();

    /// <summary>
    /// Adds a preview provider that generates a HTML preview for a specific file extension.
    /// </summary>
    Result AddPreviewProvider(PreviewProvider previewProvider);

    /// <summary>
    /// Returns a previously registered preview provider for the specified file extension.
    /// Fails if no matching preview provider is found.
    /// </summary>
    Result<PreviewProvider> GetPreviewProvider(string fileExtension);
}
