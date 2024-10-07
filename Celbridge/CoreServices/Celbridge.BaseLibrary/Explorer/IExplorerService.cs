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
    /// Returns the Resource Registry associated with the current project.
    /// </summary>
    IResourceRegistry ResourceRegistry { get; }

    /// <summary>
    /// Returns the Resource Tree View associated with the current project.
    /// </summary>
    IResourceTreeView ResourceTreeView { get; }

    /// <summary>
    /// The currenlty selected resource in the Explorer Panel.
    /// </summary>
    ResourceKey SelectedResource { get; }

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
    /// Select a resource in the explorer panel.
    /// </summary>
    Task<Result> SelectResource(ResourceKey resource, bool showExplorerPanel);

    /// <summary>
    /// Stores the selected resource in persistent storage.
    /// This resource will be selected at the start of the next editing session.
    /// </summary>
    Task StoreSelectedResource();

    /// <summary>
    /// Restores the state of the panel from the previous session.
    /// </summary>
    Task RestorePanelState();
}
