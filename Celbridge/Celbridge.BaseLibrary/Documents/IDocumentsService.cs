using Celbridge.Resources;

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
    /// Factory method to create the documents panel for the workspace UI.
    /// </summary>
    IDocumentsPanel CreateDocumentsPanel();

    /// <summary>
    /// Opens a file resource as an editable document in the documents panel.
    /// </summary>
    Task<Result> OpenDocument(ResourceKey fileResource);

    /// <summary>
    /// Closes an opened document in the documents panel.
    /// forceClose forces the document to close without allowing the document to cancel the close operation.
    /// </summary>
    Task<Result> CloseDocument(ResourceKey fileResource, bool forceClose);

    /// <summary>
    /// Save any modified documents to disk.
    /// This method is called on a timer to save modified documents at regular intervals.
    /// Delta time is the time since this method was last called.
    /// </summary>
    Task<Result> SaveModifiedDocuments(double deltaTime);

    /// <summary>
    /// Opens any documents that were opened in the previous session.
    /// </summary>
    Result OpenPreviousDocuments();
}
