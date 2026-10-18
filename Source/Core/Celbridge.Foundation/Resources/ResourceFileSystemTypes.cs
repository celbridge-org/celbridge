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
/// One resource an operation could not process, paired with the reason it failed. Carried by the
/// batch command results and by ResourceOperationFailedMessage, so the reason reaches the person at
/// the keyboard as well as a programmatic caller.
/// </summary>
public record FailedResource(
    ResourceKey Resource,
    string Message)
{
    /// <summary>
    /// The typed reason when the failure carries one, so a user-facing surface can write it in the
    /// user's language. Message stays as the operation reported it, for a programmatic caller.
    /// </summary>
    public Exception? Reason { get; init; }
}

/// <summary>
/// Result of an integrity-aware move: the outcome of the paired-sidecar cascade.
/// </summary>
public record MoveResult(
    SidecarOutcome Sidecar);

/// <summary>
/// Result of an integrity-aware copy: the outcome of the paired-sidecar cascade.
/// </summary>
public record CopyResult(
    SidecarOutcome Sidecar);

/// <summary>
/// Result of importing a folder from outside the project. LeftOutLinks lists each link the import left out,
/// keyed by where it would have landed. Each one carries a LinkNotFollowedError.
/// </summary>
public record ImportResult(
    IReadOnlyList<FailedResource> LeftOutLinks);

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
    DateTime ModifiedUtc,
    FileSystemAttributes Attributes);
