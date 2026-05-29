using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// How DeleteResourceCommand should respond when the resources being deleted
/// are referenced by other project resources.
/// </summary>
public enum DeleteReferencePolicy
{
    /// <summary>
    /// Prompt the user via IDialogService and proceed only if confirmed.
    /// </summary>
    RequireConfirmation,

    /// <summary>
    /// Refuse the batch and report the conflicting referencers in the result.
    /// </summary>
    FailIfReferenced,

    /// <summary>
    /// Proceed without prompting, leaving the existing references dangling.
    /// </summary>
    BreakReferences,
}

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
    /// The policy gate passed and execution ran but at least one resource
    /// failed mechanically (file locked, IO error, etc.); inspect ResourceResults
    /// to see which. Also covers the rare edge where every resource failed —
    /// the agent's next step is the same either way (inspect ResourceResults).
    /// </summary>
    DeletedSome,

    /// <summary>
    /// Policy-gate failure: the user declined the confirmation prompt. The
    /// filesystem is untouched.
    /// </summary>
    CancelledByUser,

    /// <summary>
    /// Policy-gate failure under FailIfReferenced: at least one resource had
    /// external referencers. The filesystem is untouched.
    /// </summary>
    BlockedByReferences,
}

/// <summary>
/// Per-resource outcome inside a DeleteResourceCommand batch. The non-Deleted
/// values are typed so an agent can branch on the cause without parsing
/// FailureMessage. The Locked / PermissionDenied / IOFailure values align
/// with the same-named concepts on ReferencerSkipReason (used by the rename
/// cascade) — delete is a single operation so it doesn't need ReferencerSkipReason's
/// ReadFailed / WriteFailed split.
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
/// Per-resource result entry inside a DeleteCommandResult. The three fields are
/// semantically independent: Outcome carries the typed result of deleting the
/// parent resource; Sidecar carries the outcome of the best-effort .cel
/// cascade; FailureMessage carries the human-readable diagnostic for the
/// parent failure (null when Outcome is Deleted). Sidecar cascade failures are
/// best-effort and surface through Sidecar + the host log, not through
/// FailureMessage.
/// </summary>
public record DeleteResourceResult(
    ResourceKey Resource,
    DeleteResourceOutcome Outcome,
    SidecarOutcome Sidecar,
    string? FailureMessage);

/// <summary>
/// Structured result produced by DeleteResourceCommand. Referencers maps each
/// input resource to the resources outside the batch that referenced it; it is
/// populated when external referencers were detected, whether the batch
/// proceeded (BreakReferences) or was gated (CancelledByUser / BlockedByReferences).
/// References from one batch resource to another are filtered out — those go
/// away alongside their target so they cannot block or be reported as dangling.
/// </summary>
public record DeleteCommandResult(
    DeleteBatchOutcome BatchOutcome,
    IReadOnlyList<DeleteResourceResult> ResourceResults,
    IReadOnlyDictionary<ResourceKey, IReadOnlyList<ResourceKey>> Referencers);

/// <summary>
/// Delete one or more file or folder resources from the project.
/// </summary>
public interface IDeleteResourceCommand : IExecutableCommand<DeleteCommandResult>
{
    /// <summary>
    /// Resources to delete.
    /// </summary>
    List<ResourceKey> Resources { get; set; }

    /// <summary>
    /// Policy applied across the batch when one or more resources are referenced
    /// by other project resources. Defaults to RequireConfirmation.
    /// </summary>
    DeleteReferencePolicy ReferencePolicy { get; set; }
}
