namespace Celbridge.Resources;

/// <summary>
/// The outcome of the sidecar cascade attached to a structural operation.
/// </summary>
public enum SidecarOutcome
{
    /// <summary>
    /// No sidecar file existed alongside the source; nothing to cascade.
    /// </summary>
    NotPresent,

    /// <summary>
    /// A sidecar existed and the operation applied to it successfully.
    /// </summary>
    Cascaded,

    /// <summary>
    /// A sidecar existed but the cascade step failed. The parent operation still succeeded.
    /// </summary>
    Failed,
}

/// <summary>
/// Why a referencer could not be rewritten during a move's cascade. The
/// ReadOnly and PermissionDenied values align with the same-named concepts
/// on DeleteResourceOutcome; ReadFailed and WriteFailed split the IOFailure
/// concept by operation phase because rewriting a referencer involves both
/// a read and a write. Inspect SkippedReferencer.Message for the specific cause.
/// </summary>
public enum ReferencerSkipReason
{
    /// <summary>
    /// Catch-all for an IO failure during the read of the referencer's bytes
    /// (file held open by another process, disk error, network share gone).
    /// </summary>
    ReadFailed,

    /// <summary>
    /// Catch-all for an IO failure during the write of the rewritten content
    /// (file briefly locked, disk full, network share gone, hardware error).
    /// </summary>
    WriteFailed,

    /// <summary>
    /// The DOS read-only attribute is set on the referencer. Trivially clearable
    /// — uncheck the read-only flag and re-run the rename.
    /// </summary>
    ReadOnly,

    /// <summary>
    /// ACL or POSIX denial. Needs the right account or admin rights.
    /// </summary>
    PermissionDenied,
}

/// <summary>
/// A referencer the move could not rewrite. The reference is left stale and
/// will surface via data_check_project; a re-run of the rename after the
/// underlying issue clears (close the editor, remove the read-only attribute)
/// picks up the residual rewrite because the FS layer is idempotent.
/// </summary>
public record SkippedReferencer(
    ResourceKey Resource,
    ReferencerSkipReason Reason,
    string Message);

/// <summary>
/// Result of an integrity-aware move: the list of resources whose references
/// were rewritten, the list of referencers the cascade had to skip (with a
/// reason for each), and the outcome of the paired-sidecar cascade.
/// </summary>
public record MoveResult(
    IReadOnlyList<ResourceKey> UpdatedReferencers,
    IReadOnlyList<SkippedReferencer> SkippedReferencers,
    SidecarOutcome Sidecar);

/// <summary>
/// Result of an integrity-aware copy: the outcome of the paired-sidecar cascade.
/// </summary>
public record CopyResult(
    SidecarOutcome Sidecar);

/// <summary>
/// Result of an integrity-aware delete: the outcome of the paired-sidecar cascade.
/// </summary>
public record DeleteResult(
    SidecarOutcome Sidecar);

/// <summary>
/// One immediate child of a folder, returned by EnumerateFolderAsync.
/// </summary>
public record FolderItem(
    ResourceKey Resource,
    bool IsFolder,
    long Size,
    DateTime ModifiedUtc);

/// <summary>
/// Discriminates the outcome of a GetInfoAsync probe.
/// </summary>
public enum StorageItemKind
{
    /// <summary>
    /// The resource does not exist at the resolved path.
    /// </summary>
    NotFound,

    /// <summary>
    /// The resource exists and is a file.
    /// </summary>
    File,

    /// <summary>
    /// The resource exists and is a folder.
    /// </summary>
    Folder,
}

/// <summary>
/// Metadata for a single resource, returned by GetInfoAsync. Size is the
/// file size in bytes for File; 0 for Folder and NotFound. ModifiedUtc is
/// the last-modified timestamp for File and Folder; default(DateTime) for
/// NotFound.
/// </summary>
public record StorageItemInfo(
    StorageItemKind Kind,
    long Size,
    DateTime ModifiedUtc);
