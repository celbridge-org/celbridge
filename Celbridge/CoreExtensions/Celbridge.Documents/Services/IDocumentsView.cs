using Celbridge.Resources;

namespace Celbridge.Documents.Services;

/// <summary>
/// Interface for interacting with the DocumentsPanelView.
/// </summary>
internal interface IDocumentsView
{
    /// <summary>
    /// Open a file document in the documents panel.
    /// </summary>
    Task<Result> OpenFileDocument(ResourceKey fileResource, string filePath);
}
