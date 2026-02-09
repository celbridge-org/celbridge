using Celbridge.Commands;
using Celbridge.DataTransfer;

namespace Celbridge.Resources;

/// <summary>
/// Copy one or more resources to a different location in the project.
/// </summary>
public interface ICopyResourceCommand : IExecutableCommand
{
    /// <summary>
    /// Resources to be copied.
    /// </summary>
    List<ResourceKey> SourceResources { get; set; }

    /// <summary>
    /// Location to move the resources to.
    /// </summary>
    ResourceKey DestResource { get; set; }

    /// <summary>
    /// Controls whether the resources are copied or moved to the new location.
    /// If the resources are moved, the resources in the original location are deleted.
    /// </summary>
    DataTransferMode TransferMode { get; set; }

    /// <summary>
    /// If a copied resource is a folder, expand the folder after moving it.
    /// </summary>
    bool ExpandCopiedFolder { get; set; }
}
