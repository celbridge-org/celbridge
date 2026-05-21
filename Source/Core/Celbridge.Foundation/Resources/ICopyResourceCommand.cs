using Celbridge.Commands;
using Celbridge.DataTransfer;

namespace Celbridge.Resources;

/// <summary>
/// Aggregated result of a CopyResourceCommand batch. UpdatedReferencers and
/// SkippedReferencers are aggregated across every move in the batch (empty
/// for copy-mode batches because copy does not rewrite references). FailedResources
/// identifies the source resources whose bytes operation failed; their entries
/// do not appear in UpdatedReferencers or SkippedReferencers.
/// </summary>
public record CopyCommandResult(
    IReadOnlyList<ResourceKey> UpdatedReferencers,
    IReadOnlyList<SkippedReferencer> SkippedReferencers,
    IReadOnlyList<ResourceKey> FailedResources);

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
