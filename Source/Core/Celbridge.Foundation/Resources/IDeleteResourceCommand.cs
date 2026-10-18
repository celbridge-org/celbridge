using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// Aggregate outcome of a DeleteResourceCommand batch.
/// </summary>
public enum DeleteBatchOutcome
{
    /// <summary>
    /// Every resource in the batch was deleted successfully.
    /// </summary>
    DeletedAll,

    /// <summary>
    /// At least one resource failed mechanically (file locked, IO error, etc.);
    /// inspect ResourceResults to see which. Also covers the rare edge where
    /// every resource failed — the agent's next step is the same either way
    /// (inspect ResourceResults).
    /// </summary>
    DeletedSome,
}

/// <summary>
/// Per-resource outcome inside a DeleteResourceCommand batch. The non-Deleted
/// values are typed so an agent can branch on the cause without parsing
/// FailureMessage.
/// </summary>
public enum DeleteResourceOutcome
{
    /// <summary>
    /// The resource was deleted successfully.
    /// </summary>
    Deleted,

    /// <summary>
    /// The resource was already gone when the operation ran — a no-op success.
    /// </summary>
    NotFound,

    /// <summary>
    /// Another process holds the file (open editor, antivirus, indexer). Often
    /// fixable by closing the offending process and retrying.
    /// </summary>
    Locked,

    /// <summary>
    /// ACL or POSIX denial. Needs the right account or admin rights.
    /// </summary>
    PermissionDenied,

    /// <summary>
    /// Catch-all for any other mechanical failure (disk full, network share
    /// gone, hardware error) that doesn't fit the more specific reasons.
    /// </summary>
    IOFailure,
}

/// <summary>
/// Per-resource result entry inside a DeleteCommandResult. The fields are
/// semantically independent. Outcome carries the typed result of deleting the
/// parent resource. Sidecar carries the outcome of the best-effort .cel
/// cascade. FailureMessage carries the human-readable diagnostic for the
/// parent failure (null when Outcome is Deleted). Sidecar cascade failures are
/// best-effort and surface through Sidecar and the host log, not through
/// FailureMessage. Undoable is true if undo can restore the deleted resource,
/// and is always false when Outcome is not Deleted.
/// </summary>
public record DeleteResourceResult(
    ResourceKey Resource,
    DeleteResourceOutcome Outcome,
    SidecarOutcome Sidecar,
    bool Undoable,
    string? FailureMessage);

/// <summary>
/// Structured result produced by DeleteResourceCommand.
/// </summary>
public record DeleteCommandResult(
    DeleteBatchOutcome BatchOutcome,
    IReadOnlyList<DeleteResourceResult> ResourceResults);

/// <summary>
/// Delete one or more file or folder resources from the project.
/// </summary>
public interface IDeleteResourceCommand : IExecutableCommand<DeleteCommandResult>
{
    /// <summary>
    /// Resources to delete.
    /// </summary>
    List<ResourceKey> Resources { get; set; }
}
