using Celbridge.DataTransfer;

namespace Celbridge.Explorer;

/// <summary>
/// Provides functionality to support the explorer panel in the workspace UI.
/// </summary>
public interface IExplorerService
{
    /// <summary>
    /// Returns the Explorer Panel view.
    /// </summary>
    IExplorerPanel? ExplorerPanel { get; }

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
    IExplorerPanel CreateExplorerPanel();

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

    /// <summary>
    /// Returns the selected resource in the explorer panel.
    /// Returns an empty resource if no resource is currently selected.
    /// </summary>
    ResourceKey GetSelectedResource();

    /// <summary>
    /// Select a resource in the explorer panel.
    /// </summary>
    Result SetSelectedResource(ResourceKey resource);
}
