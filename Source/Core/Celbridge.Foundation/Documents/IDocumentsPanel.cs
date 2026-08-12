namespace Celbridge.Documents;

/// <summary>
/// Document hosting operations on the workspace panel: opening, closing and addressing documents in
/// the three document areas.
/// </summary>
public interface IDocumentsPanel
{
    /// <summary>
    /// The sections that are currently mounted, in reading order. A section is mounted when its area is
    /// visible and, for a secondary section, that area is split.
    /// </summary>
    IReadOnlyList<DocumentSection> VisibleSections { get; }

    /// <summary>
    /// Gets or sets the active document that is being inspected.
    /// </summary>
    ResourceKey ActiveDocument { get; set; }

    /// <summary>
    /// Gives the active document keyboard focus, for the paths that make a document active without an
    /// interaction that carries focus to it: a workspace restore and a layout-mode change.
    /// </summary>
    void FocusActiveDocument();

    /// <summary>
    /// Whether the area is currently showing both of its sections.
    /// </summary>
    bool IsAreaSplit(DocumentArea area);

    /// <summary>
    /// Splits the area into two sections, or folds its secondary section back into the primary one.
    /// Folding migrates any tabs in the secondary section rather than closing them.
    /// </summary>
    void SetAreaSplit(DocumentArea area, bool isSplit);

    /// <summary>
    /// Folds a split area back when either of its sections has run out of documents, so a split section is
    /// never left empty. The surviving documents always end up in the primary section.
    /// </summary>
    void ReconcileAreaSplit(DocumentArea area);

    /// <summary>
    /// The share of a split area taken by its primary section, as a value between 0 and 1.
    /// </summary>
    double GetAreaSplitRatio(DocumentArea area);

    /// <summary>
    /// Sets the share of a split area taken by its primary section, as a value between 0 and 1.
    /// </summary>
    void SetAreaSplitRatio(DocumentArea area, double ratio);

    /// <summary>
    /// Folds every area back to a single section and restores equal split positions.
    /// </summary>
    Task ResetAreaLayoutAsync();

    /// <summary>
    /// Returns a snapshot of all open documents with their addresses and editor IDs.
    /// </summary>
    IReadOnlyList<OpenDocumentInfo> GetOpenDocuments();

    /// <summary>
    /// Gets the document view for an already-opened document.
    /// Returns null if the document is not open.
    /// </summary>
    IDocumentView? GetDocumentView(ResourceKey fileResource);

    /// <summary>
    /// Open a file resource as a document in the documents panel.
    /// </summary>
    Task<Result<OpenDocumentOutcome>> OpenDocument(ResourceKey fileResource, OpenDocumentOptions? options = null);

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
