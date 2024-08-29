using Celbridge.Resources;

namespace Celbridge.Documents;

/// <summary>
/// Interface for interacting with the DocumentsPanel view.
/// </summary>
public interface IDocumentsPanel
{
    /// <summary>
    /// Open a file resource as a document in the documents panel.
    /// </summary>
    Task<Result> OpenDocument(ResourceKey fileResource, string filePath);

    /// <summary>
    /// Close an opened document in the documents panel.
    /// </summary>
    Task<Result> CloseDocument(ResourceKey fileResource);

    /// <summary>
    /// Save any modified documents to disk.
    /// </summary>    
    Task<Result> SaveModifiedDocuments(double deltaTime);
}
