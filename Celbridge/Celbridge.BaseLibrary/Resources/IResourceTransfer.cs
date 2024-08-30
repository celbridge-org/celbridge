using Celbridge.DataTransfer;

namespace Celbridge.Resources;

/// <summary>
/// Describes the transfer (move or copy) of a set of file or folder resources.
/// </summary>
public interface IResourceTransfer
{
    /// <summary>
    /// Specifies whether the resources should be copied or moved when transfered.
    /// </summary>
    DataTransferMode TransferMode { get; set; }

    /// <summary>
    /// The resource items to be transferred.
    /// </summary>
    List<ResourceTransferItem> TransferItems { get; }
}
