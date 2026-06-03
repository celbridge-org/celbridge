namespace Celbridge.Resources;

/// <summary>
/// Service for managing project resources including the resource registry, 
/// resource monitoring, and resource transfer operations.
/// </summary>
public interface IResourceService
{
    /// <summary>
    /// Returns the Resource Registry associated with the current project.
    /// </summary>
    IResourceRegistry Registry { get; }

    /// <summary>
    /// Returns the registry of resource root handlers for the current project.
    /// Use this rather than IResourceRegistry when only cross-root path/key
    /// dispatch is required.
    /// </summary>
    IRootHandlerRegistry RootHandlerRegistry { get; }

    /// <summary>
    /// Returns the Resource Monitor associated with the current project.
    /// </summary>
    IResourceMonitor Monitor { get; }

    /// <summary>
    /// Returns the Resource Transfer Service associated with the workspace.
    /// </summary>
    IResourceTransferService TransferService { get; }

    /// <summary>
    /// Returns the Resource Operation Service associated with the workspace.
    /// </summary>
    IResourceOperationService OperationService { get; }

    /// <summary>
    /// Returns the resource-key-aware file-system gateway for the workspace.
    /// </summary>
    IResourceFileSystem FileSystem { get; }

    /// <summary>
    /// Returns the workspace's resource policy engine. Decides whether a given
    /// (resource, action) is allowed by the project's [resources] configuration
    /// and the built-in default rules.
    /// </summary>
    IResourcePolicy Policy { get; }

    /// <summary>
    /// Returns the soft-delete trash service: move-to-trash, restore, and purge
    /// operations used by the resource operation service for undoable deletes.
    /// </summary>
    ITrashService TrashService { get; }

    /// <summary>
    /// Returns the on-demand scanner over project text and sidecar files,
    /// used by the rename cascade, tag queries, and the project-health check.
    /// </summary>
    IResourceScanner Scanner { get; }

    /// <summary>
    /// Returns the sidecar service: validation helpers plus read / mutate /
    /// write operations over .cel sidecar files via the file-system gateway.
    /// </summary>
    ISidecarService SidecarService { get; }

    /// <summary>
    /// Schedules a resource update.
    /// The update occurs after a short quiet period to coalesce rapid calls from multiple sources.
    /// </summary>
    void ScheduleResourceUpdate();

    /// <summary>
    /// Refreshes the resource registry immediately.
    /// </summary>
    Task<Result> UpdateResourcesAsync();
}
