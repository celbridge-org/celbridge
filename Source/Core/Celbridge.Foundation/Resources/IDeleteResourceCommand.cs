using Celbridge.Commands;

namespace Celbridge.Resources;

/// <summary>
/// How DeleteResourceCommand should respond when the resources being deleted are
/// referenced by other project resources. RequireConfirmation prompts the user
/// via IDialogService; FailIfReferenced refuses the batch and reports
/// the conflicting referencers; BreakReferences proceeds without prompting,
/// leaving the existing references dangling.
/// </summary>
public enum DeleteReferencePolicy
{
    RequireConfirmation,
    FailIfReferenced,
    BreakReferences,
}

/// <summary>
/// Aggregate outcome of a DeleteResourceCommand batch.
/// DeletedAll means every resource in the batch was deleted successfully.
/// DeletedSome means the policy gate passed and execution ran but at least
/// one resource failed mechanically (file locked, IO error, etc.); inspect
/// ResourceResults to see which. This also covers the rare edge where every
/// resource failed — when zero of N succeed, the batch is still classified
/// DeletedSome rather than carving out a separate "none succeeded" value,
/// since the agent's next step (inspect ResourceResults) is the same either way.
/// CancelledByUser and BlockedByReferences are policy-gate failures that leave
/// the filesystem untouched.
/// </summary>
public enum DeleteBatchOutcome
{
    DeletedAll,
    DeletedSome,
    CancelledByUser,
    BlockedByReferences,
}

/// <summary>
/// Per-resource outcome inside a DeleteResourceCommand batch. The non-Deleted
/// values are typed so an agent can branch on the cause without parsing
/// FailureMessage: NotFound is the no-op success case (the resource is already
/// gone); Locked means another process holds the file (often fixable by closing
/// the editor or stopping the antivirus); PermissionDenied is an ACL / POSIX
/// denial (needs the right account or admin); IOFailure is the catch-all for
/// disk full, network share gone, hardware error, and any other mechanical
/// failure that doesn't fit the more specific reasons.
/// </summary>
public enum DeleteResourceOutcome
{
    Deleted,
    NotFound,
    Locked,
    PermissionDenied,
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
