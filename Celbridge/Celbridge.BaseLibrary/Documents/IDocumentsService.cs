using Celbridge.Resources;

namespace Celbridge.Documents;

/// <summary>
/// The documents service provides functionality to support the documents panel in the workspace UI.
/// </summary>
public interface IDocumentsService
{
    /// <summary>
    /// Factory method to create the documents panel for the workspace UI.
    /// </summary>
    object CreateDocumentsPanel();

    /// <summary>
    /// Opens a file resource as an editable document in the documents panel.
    /// </summary>
    Task<Result> OpenDocument(ResourceKey fileResource);

    /// <summary>
    /// Closes an opened document in the documents panel.
    /// </summary>
    Task<Result> CloseDocument(ResourceKey fileResource);

    /// <summary>
    /// Save any modified documents to disk.
    /// This method is called on a timer to save modified documents at regular intervals.
    /// Delta time is the time since this method was last called.
    /// </summary>
    Task<Result> SaveModifiedDocuments(double deltaTime);
}
