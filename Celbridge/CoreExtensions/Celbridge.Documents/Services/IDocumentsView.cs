using Celbridge.Resources;

namespace Celbridge.Documents.Services;

/// <summary>
/// Interface for interacting with the DocumentsPanelView.
/// </summary>
internal interface IDocumentsView
{
    /// <summary>
    /// Open a file resource as a document in the documents panel.
    /// </summary>
    Task<Result> OpenDocument(ResourceKey fileResource, string filePath);

    /// <summary>
    /// Close an opened document in the documents panel.
    /// </summary>
    Task<Result> CloseDocument(ResourceKey fileResource);
}
