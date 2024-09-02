using Celbridge.Commands;
using Celbridge.Explorer;

namespace Celbridge.DataTransfer;

/// <summary>
/// Copies a resource to the clipboard.
/// </summary>
public interface ICopyResourceToClipboardCommand : IExecutableCommand
{
    /// <summary>
    /// Resource to copy to the clipboard.
    /// </summary>
    ResourceKey SourceResource { get; set; }

    /// <summary>
    /// Specifies if the resource is copied or cut to the clipboard.
    /// </summary>
    DataTransferMode TransferMode { get; set; }
}
