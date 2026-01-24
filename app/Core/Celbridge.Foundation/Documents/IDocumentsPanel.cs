namespace Celbridge.Documents;

/// <summary>
/// Interface for interacting with the DocumentsPanel view.
/// </summary>
public interface IDocumentsPanel
{
    /// <summary>
    /// Returns a list of open documents.
    /// The list is in the same order as the document tabs in the documents panel.
    /// </summary>
    List<ResourceKey> GetOpenDocuments();

    /// <summary>
    /// Open a file resource as a document in the documents panel.
    /// </summary>
    Task<Result> OpenDocument(ResourceKey fileResource, string filePath, bool forceReload);

    /// <summary>
    /// Open a file resource as a document in the documents panel and navigate to a specific location.
    /// </summary>
    Task<Result> OpenDocument(ResourceKey fileResource, string filePath, bool forceReload, string location);

    /// <summary>
    /// Close an opened document in the documents panel.
    /// forceClose forces the document to close without allowing the document to cancel the close operation.
    /// </summary>
    Task<Result> CloseDocument(ResourceKey fileResource, bool forceClose);

    /// <summary>
    /// Save any modified documents to disk.
    /// </summary>    
    Task<Result> SaveModifiedDocuments(double deltaTime);

    /// <summary>
    /// Selects an opened document in the documents panel.
    /// Fails if the specified document is not opened.
    /// </summary>
    Result SelectDocument(ResourceKey fileResource);

    /// <summary>
    /// Navigate to a specific location within an already-opened document.
    /// </summary>
    Task<Result> NavigateToLocation(ResourceKey fileResource, string location);

    /// <summary>
    /// Change the resource of an opened document.
    /// </summary>
    Task<Result> ChangeDocumentResource(ResourceKey oldResource, DocumentViewType oldDocumentType, ResourceKey newResource, string newResourcePath, DocumentViewType newDocumentType);

    /// <summary>
    /// Closes all open documents and cleans up their resources. Called when the workspace is being unloaded.
    /// </summary>
    void Shutdown();
}
