using Celbridge.DataTransfer;

namespace Celbridge.Resources;

/// <summary>
/// Service for creating and executing resource transfer operations, plus the
/// destination-resolution helpers shared by drag/drop, paste, and the
/// transfer commands.
/// </summary>
public interface IResourceTransferService
{
    /// <summary>
    /// Create a Resource Transfer object describing the transfer of resources from a list of source paths to a destination folder.
    /// </summary>
    Result<IResourceTransfer> CreateResourceTransfer(List<string> sourcePaths, ResourceKey destFolderResource, DataTransferMode transferMode);

    /// <summary>
    /// Transfer resources to a destination folder resource.
    /// </summary>
    Result TransferResources(ResourceKey destFolderResource, IResourceTransfer transfer);

    /// <summary>
    /// Returns a resolved destination resource key for a resource transfer.
    /// If destResource specifies an existing folder in the project then the
    /// source resource name is appended; otherwise destResource is returned
    /// unchanged.
    /// </summary>
    ResourceKey ResolveDestinationResource(ResourceKey sourceResource, ResourceKey destResource);

    /// <summary>
    /// Returns a resolved destination resource key for a resource transfer
    /// from an external source path. When destResource is an existing folder
    /// the source filename is appended; otherwise destResource is returned
    /// unchanged.
    /// </summary>
    ResourceKey ResolveSourcePathDestinationResource(string sourcePath, ResourceKey destResource);

    /// <summary>
    /// Returns the folder resource associated with the context menu item for
    /// a resource. Folder → itself; file → its parent folder; null → the
    /// project folder.
    /// </summary>
    ResourceKey GetContextMenuItemFolder(IResource? resource);
}
