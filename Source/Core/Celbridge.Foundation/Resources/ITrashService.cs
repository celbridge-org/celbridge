namespace Celbridge.Resources;

/// <summary>
/// A single entity data file that was moved into the trash alongside a parent
/// resource. Carries both ends of the move so restore and purge can act on it.
/// </summary>
public record TrashedEntityDataFile(string OriginalPath, string TrashPath);

/// <summary>
/// Bookkeeping for one soft-delete. Captures every path the trash service moved
/// so a later RestoreFromTrashAsync or PurgeAsync can act on the exact same set.
/// DescendantKeys are the resource keys of every file under the deleted folder,
/// captured before the move so consumers can broadcast removal messages.
/// </summary>
public record TrashEntry(
    ResourceKey OriginalResource,
    string TrashId,
    bool WasFolder,
    bool WasEmptyFolder,
    string OriginalPath,
    string TrashPath,
    string? SidecarOriginalPath,
    string? SidecarTrashPath,
    IReadOnlyList<TrashedEntityDataFile> EntityDataFiles,
    IReadOnlyList<ResourceKey> DescendantKeys);

/// <summary>
/// The workspace-scoped soft-delete service. Moves resources into the workspace
/// trash folder, restores them on undo, and purges them when an undo operation
/// is evicted from the undo stack or the redo stack is cleared. Owns the trash
/// folder layout and the read-only attribute handling for delete-as-override.
/// </summary>
public interface ITrashService
{
    /// <summary>
    /// Moves the resource into the workspace trash folder. Cascades the paired
    /// sidecar and any entity data files. Returns a TrashEntry that uniquely
    /// identifies the soft-delete for later restore or purge.
    /// </summary>
    Task<Result<TrashEntry>> MoveToTrashAsync(ResourceKey resource);

    /// <summary>
    /// Restores a previously-trashed resource to its original location, including
    /// any sidecar and entity data files captured at trash time.
    /// </summary>
    Task<Result> RestoreFromTrashAsync(TrashEntry entry);

    /// <summary>
    /// Permanently removes a trashed resource and its associated files. Called
    /// when an undo operation is evicted from the undo stack or when the redo
    /// stack is cleared by a new operation.
    /// </summary>
    Task<Result> PurgeAsync(TrashEntry entry);
}
