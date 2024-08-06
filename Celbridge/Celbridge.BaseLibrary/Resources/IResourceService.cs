namespace Celbridge.Resources;

/// <summary>
/// The project service provides functionality to support the project panel in the workspace UI.
/// </summary>
public interface IResourceService
{
    /// <summary>
    /// Returns the Resource Registry associated with the loaded project.
    /// </summary>
    IResourceRegistry ResourceRegistry { get; }

    /// <summary>
    /// Returns the Resource Tree View associated with the loaded project.
    /// </summary>
    IResourceTreeView ResourceTreeView { get; }

    /// <summary>
    /// Factory method to create the resources panel for the workspace UI.
    /// </summary>
    object CreateResourcesPanel();

    /// <summary>
    /// Update the resource registry and populate the resource tree view.
    /// </summary>
    Task<Result> UpdateResourcesAsync();

    /// <summary>
    /// Create a list of ResourceTransferItems from a list of resource paths to be transfered and a destination folder resource.
    /// </summary>
    Result<List<ResourceTransferItem>> CreateResourceTransferItems(ResourceKey destFolderResource, List<string> resourcePaths);

    /// <summary>
    /// Transfer resources to a destination folder resource.
    /// </summary>
    Task<Result> TransferResources(ResourceKey destFolderResource, IResourceTransfer transfer);
}
