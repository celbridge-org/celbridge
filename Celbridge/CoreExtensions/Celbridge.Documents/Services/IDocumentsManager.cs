using Celbridge.Resources;

namespace Celbridge.Documents.Services;

/// <summary>
/// Interface for interacting with the DocumentsPanelViewModel.
/// </summary>
internal interface IDocumentsManager
{
    /// <summary>
    /// Open a file document in the documents panel.
    /// </summary>
    Task<Result> OpenFileDocument(ResourceKey fileResource);
}
