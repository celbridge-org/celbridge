using Celbridge.Resources;

namespace Celbridge.Documents.Services;

/// <summary>
/// Interface for interacting with the DocumentsPanelViewModel.
/// </summary>
internal interface IDocumentsManager
{
    /// <summary>
    /// Open a file resource as a document in the documents panel.
    /// </summary>
    Task<Result> OpenDocument(ResourceKey fileResource);

    /// <summary>
    /// Close an opened document in the documents panel.
    /// </summary>
    Task<Result> CloseDocument(ResourceKey fileResource);
}
