using Celbridge.Projects;

namespace Celbridge.Clipboard;

/// <summary>
/// Describes the transfer (move or copy) of a set of file or folder resources.
/// </summary>
public interface IResourceTransfer
{
    /// <summary>
    /// Specifies whether the resources should be copied or moved when transfered.
    /// </summary>
    ResourceTransferMode TransferMode { get; }

    /// <summary>
    /// The resource items to be transferred.
    /// </summary>
    List<ResourceTransferItem> TransferItems { get; }
}
