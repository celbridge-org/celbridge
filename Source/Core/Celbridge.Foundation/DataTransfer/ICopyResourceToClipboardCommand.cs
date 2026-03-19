using Celbridge.Commands;

namespace Celbridge.DataTransfer;

/// <summary>
/// Copies one or more resources to the clipboard.
/// </summary>
public interface ICopyResourceToClipboardCommand : IExecutableCommand
{
    /// <summary>
    /// Resources to copy to the clipboard.
    /// </summary>
    List<ResourceKey> SourceResources { get; set; }

    /// <summary>
    /// Specifies if the resources are copied or cut to the clipboard.
    /// </summary>
    DataTransferMode TransferMode { get; set; }
}
