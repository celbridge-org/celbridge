namespace Celbridge.Documents;

/// <summary>
/// Interface for interacting with the DocumentsPanel view.
/// </summary>
public interface IDocumentsPanel
{
    /// <summary>
    /// Gets or sets the number of document sections (1-3).
    /// </summary>
    int SectionCount { get; set; }

    /// <summary>
    /// Sets the proportional widths (ratios) of document sections.
    /// Ratios are relative values that sum to 1.0.
    /// </summary>
    void SetSectionRatios(List<double> ratios);

    /// <summary>
    /// Returns all open documents with their addresses (UI positions).
    /// </summary>
    Dictionary<ResourceKey, DocumentAddress> GetDocumentAddresses();

    /// <summary>
    /// Open a file resource as a document in the documents panel.
    /// Opens in the default section (section 0).
    /// </summary>
    Task<Result> OpenDocument(ResourceKey fileResource, string filePath, bool forceReload);

    /// <summary>
    /// Open a file resource as a document in the documents panel and navigate to a specific location.
    /// </summary>
    Task<Result> OpenDocument(ResourceKey fileResource, string filePath, bool forceReload, string location);

    /// <summary>
    /// Open a file resource as a document at a specific address (UI position).
    /// </summary>
    Task<Result> OpenDocumentAtAddress(ResourceKey fileResource, string filePath, DocumentAddress address);

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
