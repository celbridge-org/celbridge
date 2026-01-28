using Celbridge.DataTransfer;

namespace Celbridge.Resources;

/// <summary>
/// Service for creating and executing resource transfer operations.
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
}
