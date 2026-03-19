using Celbridge.Commands;
using Celbridge.DataTransfer;

namespace Celbridge.Resources;

/// <summary>
/// Transfer one or more file or folder resources to a destination folder
/// Batched as a single undo operation.
/// </summary>
public interface ITransferResourcesCommand : IExecutableCommand
{
    /// <summary>
    /// The destination folder to transfer resources into.
    /// </summary>
    ResourceKey DestFolderResource { get; set; }

    /// <summary>
    /// Controls whether resources are copied or moved to the destination.
    /// </summary>
    DataTransferMode TransferMode { get; set; }

    /// <summary>
    /// The resource items to be transferred.
    /// </summary>
    List<ResourceTransferItem> TransferItems { get; set; }
}
