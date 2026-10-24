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
    Task<Result<IResourceTransfer>> CreateResourceTransferAsync(List<string> sourcePaths, ResourceKey destFolderResource, DataTransferMode transferMode);

    /// <summary>
    /// Transfer resources to a destination folder resource.
    /// </summary>
    Result TransferResources(ResourceKey destFolderResource, IResourceTransfer transfer);

    /// <summary>
    /// Returns a resolved destination resource key for a resource transfer.
    /// If destResource is a folder on disk then the source resource name is
    /// appended. Otherwise destResource is returned unchanged.
    /// </summary>
    Task<ResourceKey> ResolveDestinationResourceAsync(ResourceKey sourceResource, ResourceKey destResource);
}
