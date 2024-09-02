using Celbridge.Explorer;

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
    /// Stores the list of previous open documents in persistent storage.
    /// These documents will be opened at the start of the next editing session.
    /// </summary>
    Task SetPreviousOpenDocuments(List<ResourceKey> openDocuments);

    /// <summary>
    /// Stores the previous selected document in persistent storage.
    /// This document will be selected at the start of the next editing session.
    /// </summary>
    Task SetPreviousSelectedDocument(ResourceKey selectedDocument);

    /// <summary>
    /// Reopens documents that were left open in the previous session.
    /// The method completes once the commands to open these documents have been scheduled.
    /// Note that it does not wait for the documents to fully open.
    /// </summary>
    Task<Result> OpenPreviousDocuments();
}
