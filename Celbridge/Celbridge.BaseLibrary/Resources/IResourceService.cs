using Celbridge.DataTransfer;

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
    /// Create a Resource Transfer object describing the transfer of resources from a list of source paths to a destination folder.
    /// </summary>
    Result<IResourceTransfer> CreateResourceTransfer(List<string> sourcePaths, ResourceKey destFolderResource, DataTransferMode transferMode);

    /// <summary>
    /// Transfer resources to a destination folder resource.
    /// </summary>
    Task<Result> TransferResources(ResourceKey destFolderResource, IResourceTransfer transfer);
}
