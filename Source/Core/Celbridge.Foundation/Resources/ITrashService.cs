namespace Celbridge.Resources;

/// <summary>
/// Bookkeeping for one soft-delete. Captures every path the trash service moved
/// so a later RestoreFromTrashAsync or PurgeAsync can act on the exact same set.
/// </summary>
public record TrashEntry(
    ResourceKey OriginalResource,
    string TrashId,
    bool WasFolder,
    bool WasEmptyFolder,
    string OriginalPath,
    string TrashPath,
    string? SidecarOriginalPath,
    string? SidecarTrashPath);

/// <summary>
/// Attached to the failed Result when the trash refuses a link, or a folder that
/// contains a link.
/// </summary>
public sealed class ResourceContainsLinkError : Exception
{
    /// <summary>
    /// The path of the link the trash found.
    /// </summary>
    public string LinkPath { get; }

    public ResourceContainsLinkError(string linkPath)
        : base($"The trash does not accept links: '{linkPath}'")
    {
        LinkPath = linkPath;
    }
}

/// <summary>
/// The workspace-scoped soft-delete service for resources under the project root.
/// Moves resources into the workspace trash folder, restores them on undo, and
/// purges them when an undo operation is evicted from the undo stack or the redo
/// stack is cleared. Owns the trash folder layout and the read-only attribute
/// handling for delete-as-override.
/// </summary>
public interface ITrashService
{
    /// <summary>
    /// Moves the resource into the workspace trash folder. The resource must be under
    /// the project root. Cascades the paired sidecar. Returns a TrashEntry that uniquely
    /// identifies the soft-delete for later restore or purge. Refuses a link, or a
    /// folder that contains a link, with a ResourceContainsLinkError and leaves it in
    /// place.
    /// </summary>
    Task<Result<TrashEntry>> MoveToTrashAsync(ResourceKey resource);

    /// <summary>
    /// Restores a previously-trashed resource to its original location, including
    /// any sidecar captured at trash time.
    /// </summary>
    Task<Result> RestoreFromTrashAsync(TrashEntry entry);

    /// <summary>
    /// Permanently removes a trashed resource and its associated files. Called
    /// when an undo operation is evicted from the undo stack or when the redo
    /// stack is cleared by a new operation.
    /// </summary>
    Task<Result> PurgeAsync(TrashEntry entry);
}
