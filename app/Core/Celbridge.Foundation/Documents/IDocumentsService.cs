namespace Celbridge.Documents;

/// <summary>
/// The documents service provides functionality to support the documents panel in the workspace UI.
/// </summary>
public interface IDocumentsService
{
    /// <summary>
    /// The resource key for the currently selected document.
    /// This is the empty resource if no document is currently selected.
    /// </summary>
    ResourceKey SelectedDocument { get; }

    /// <summary>
    /// Gets all open documents with their addresses (UI positions).
    /// </summary>
    Dictionary<ResourceKey, DocumentAddress> DocumentAddresses { get; }

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
    Task<Result> OpenDocument(ResourceKey fileResource, bool forceReload);

    /// <summary>
    /// Opens a file resource as a document in the documents panel and navigates to a specific location.
    /// </summary>
    Task<Result> OpenDocument(ResourceKey fileResource, bool forceReload, string location);

    /// <summary>
    /// Opens a file resource as a document in a specific section of the documents panel.
    /// If the document is already open in another section, it will be moved to the target section.
    /// </summary>
    Task<Result> OpenDocumentAtSection(ResourceKey fileResource, bool forceReload, string location, int sectionIndex);

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
    /// Writes text content to the specified file resource.
    /// If the document is open for editing, the updated content is displayed in the document view.
    /// The file on disk is updated regardless of whether the document is open for editing or not.
    /// </summary>
    Task<Result> SetTextDocumentContentAsync(ResourceKey fileResource, string content);

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
    Result AddPreviewProvider(string fileExtension, IPreviewProvider previewProvider);

    /// <summary>
    /// Returns a previously registered preview provider for the specified file extension.
    /// Fails if no matching preview provider is found.
    /// </summary>
    Result<IPreviewProvider> GetPreviewProvider(string fileExtension);
}
