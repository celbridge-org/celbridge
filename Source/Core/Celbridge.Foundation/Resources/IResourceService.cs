namespace Celbridge.Resources;

/// <summary>
/// Service for managing project resources including resource monitoring
/// and resource transfer operations.
/// </summary>
public interface IResourceService
{
    /// <summary>
    /// Returns the registry of resource root handlers for the current project.
    /// The registry maps resource keys to absolute paths and back, in every root.
    /// </summary>
    IRootHandlerRegistry RootHandlers { get; }

    /// <summary>
    /// Returns the Resource Monitor associated with the current project.
    /// </summary>
    IResourceMonitor Monitor { get; }

    /// <summary>
    /// Returns the Resource Transfer Service associated with the workspace.
    /// </summary>
    IResourceTransferService Transfers { get; }

    /// <summary>
    /// Returns the Resource Operation Service associated with the workspace.
    /// </summary>
    IResourceOperationService Operations { get; }

    /// <summary>
    /// Returns the resource-key-aware file-system gateway for the workspace.
    /// </summary>
    IResourceFileSystem FileSystem { get; }

    /// <summary>
    /// Returns the workspace's resource policy engine: the compiled view of the
    /// project's [celbridge.resources] settings, plus the access invariants.
    /// </summary>
    IResourcePolicy Policy { get; }

    /// <summary>
    /// Returns the soft-delete trash service: move-to-trash, restore, and purge
    /// operations used by the resource operation service for undoable deletes.
    /// </summary>
    ITrashService Trash { get; }

    /// <summary>
    /// Returns the on-demand scanner that answers tag queries over the
    /// project's sidecar files.
    /// </summary>
    IResourceScanner Scanner { get; }

    /// <summary>
    /// Returns the walker that lists a folder and every folder below it.
    /// </summary>
    IResourceWalker Walker { get; }

    /// <summary>
    /// Returns the sidecar service: validation helpers plus read / mutate /
    /// write operations over .cel sidecar files via the file-system gateway.
    /// </summary>
    ISidecarService Sidecars { get; }
}
