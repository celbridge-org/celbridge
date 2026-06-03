using Celbridge.Commands;
using Celbridge.DataTransfer;

namespace Celbridge.Resources;

/// <summary>
/// A source resource whose copy or move failed, paired with the reason. Carried
/// in CopyCommandResult so a partial-batch failure surfaces per-resource detail
/// rather than collapsing the whole batch into a single command failure.
/// </summary>
public record FailedResource(
    ResourceKey Resource,
    string Message);

/// <summary>
/// Aggregated result of a CopyResourceCommand batch. UpdatedReferencers and
/// SkippedReferencers are aggregated across every move in the batch (empty
/// for copy-mode batches because copy does not rewrite references). FailedResources
/// identifies the source resources whose operation failed, each with its reason;
/// their entries do not appear in UpdatedReferencers or SkippedReferencers. A
/// non-empty FailedResources is a partial-batch outcome, not a command failure:
/// the command still returns Result.Ok so the resources that did succeed are
/// committed, mirroring DeleteResourceCommand's typed per-resource outcomes.
/// </summary>
public record CopyCommandResult(
    IReadOnlyList<ResourceKey> UpdatedReferencers,
    IReadOnlyList<SkippedReferencer> SkippedReferencers,
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
