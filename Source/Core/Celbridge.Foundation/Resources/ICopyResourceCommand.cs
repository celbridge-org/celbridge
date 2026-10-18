using Celbridge.Commands;
using Celbridge.DataTransfer;

namespace Celbridge.Resources;

/// <summary>
/// Aggregated result of a CopyResourceCommand batch. FailedResources lists each
/// source resource that failed to copy or move, with the reason. A non-empty
/// FailedResources is a partial-batch outcome, not a command failure. The
/// command still returns Result.Ok, and the resources that succeeded stay
/// committed.
/// </summary>
public record CopyCommandResult(
    IReadOnlyList<FailedResource> FailedResources);

/// <summary>
/// Copy one or more resources to a different location in the project.
/// </summary>
public interface ICopyResourceCommand : IExecutableCommand<CopyCommandResult>
{
    /// <summary>
    /// Resources to be copied.
    /// </summary>
    List<ResourceKey> SourceResources { get; set; }

    /// <summary>
    /// Destination location for the copy or move.
    /// </summary>
    ResourceKey DestResource { get; set; }

    /// <summary>
    /// Controls whether the resources are copied or moved to the new location.
    /// If the resources are moved, the resources in the original location are deleted.
    /// </summary>
    DataTransferMode TransferMode { get; set; }

    /// <summary>
    /// If a copied resource is a folder, expand the folder after the copy or move.
    /// </summary>
    bool ExpandCopiedFolder { get; set; }
}
