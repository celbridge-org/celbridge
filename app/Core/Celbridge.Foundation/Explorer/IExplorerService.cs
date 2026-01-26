using Celbridge.DataTransfer;
using Celbridge.UserInterface;

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
    /// Returns the Explorer Panel view.
    /// Todo: Move this to a separate Search module.
    /// </summary>
    ISearchPanel? SearchPanel { get; }

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
    /// Schedules a resource update. 
    /// The update occurs after a short quiet period to coalesce rapid calls from multiple sources.
    /// </summary>
    void ScheduleResourceUpdate();

    /// <summary>
    /// Refreshes the resource registry and tree view immediately.
    /// </summary>
    Task<Result> UpdateResourcesAsync();

    /// <summary>
    /// Create a Resource Transfer object describing the transfer of resources from a list of source paths to a destination folder.
    /// </summary>
    Result<IResourceTransfer> CreateResourceTransfer(List<string> sourcePaths, ResourceKey destFolderResource, DataTransferMode transferMode);

    /// <summary>
    /// Transfer resources to a destination folder resource.
    /// </summary>
    Result TransferResources(ResourceKey destFolderResource, IResourceTransfer transfer);

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

    /// <summary>
    /// Open the specified resource in the system file manager.
    /// </summary>
    Task<Result> OpenFileManager(ResourceKey resource);

    /// <summary>
    /// Performs an Open on the given resource.
    /// The resource will be opened in the manner appropriate to it's type, either in a browser, or in it's related application.
    /// A command will be added to the queue to do this, calling to one of the two methods below.
    /// </summary>
    void OpenResource(ResourceKey resource);

    /// <summary>
    /// Open the specified resource in the associated application.
    /// </summary>
    Task<Result> OpenApplication(ResourceKey resource);

    /// <summary>
    /// Open the specified URL in the system default browser.
    /// </summary>
    Task<Result> OpenBrowser(string uRL);

    /// <summary>
    /// Get an icon definition for the specified resource.
    /// </summary>
    FileIconDefinition GetIconForResource(ResourceKey resource);
}
