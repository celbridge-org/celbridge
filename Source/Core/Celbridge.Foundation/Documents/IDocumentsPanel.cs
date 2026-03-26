namespace Celbridge.Documents;

/// <summary>
/// Interface for interacting with the DocumentsPanel view.
/// </summary>
public interface IDocumentsPanel
{
    /// <summary>
    /// Gets or sets the number of document sections.
    /// </summary>
    int SectionCount { get; set; }

    /// <summary>
    /// Gets or sets the active document that is being inspected.
    /// </summary>
    ResourceKey ActiveDocument { get; set; }

    /// <summary>
    /// Sets the proportional widths (ratios) of document sections.
    /// Ratios are relative values that sum to 1.0.
    /// </summary>
    void SetSectionRatios(List<double> ratios);

    /// <summary>
    /// Resets all document sections to equal widths.
    /// </summary>
    Task ResetSectionRatiosAsync();

    /// <summary>
    /// Returns all open documents with their addresses (UI positions).
    /// </summary>
    Dictionary<ResourceKey, DocumentAddress> GetDocumentAddresses();

    /// <summary>
    /// Gets the document view for an already-opened document.
    /// Returns null if the document is not open.
    /// </summary>
    IDocumentView? GetDocumentView(ResourceKey fileResource);

    /// <summary>
    /// Open a file resource as a document in the documents panel and optionally navigate to a specific location.
    /// When activate is true, the document becomes the active tab.
    /// </summary>
    Task<Result> OpenDocument(ResourceKey fileResource, string filePath, bool forceReload, string location = "", bool activate = true);

    /// <summary>
    /// Open a file resource as a document at a specific address (UI position).
    /// When activate is true, the document becomes the active tab.
    /// </summary>
    Task<Result> OpenDocumentAtAddress(ResourceKey fileResource, string filePath, DocumentAddress address, bool activate = true);

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
    /// Activates an opened document in the documents panel, making it the active tab.
    /// Fails if the specified document is not opened.
    /// </summary>
    Result ActivateDocument(ResourceKey fileResource);

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
