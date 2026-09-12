using Celbridge.DataTransfer;

namespace Celbridge.Resources;

/// <summary>
/// In-progress batch of resource operations. Disposing the scope commits the
/// accumulated operations as a single undo unit; partial batches commit so
/// the user can Ctrl+Z to reverse them.
/// </summary>
public interface IBatchScope : IDisposable
{
}

/// <summary>
/// Whether a resource can be edited, and if not, why. The non-Writable
/// values name the source.
/// </summary>
public enum WritableState
{
    /// <summary>
    /// The resource accepts edits.
    /// </summary>
    Writable,

    /// <summary>
    /// The underlying file carries the OS read-only attribute.
    /// </summary>
    ReadOnlyAttribute,

    /// <summary>
    /// The resource's root handler declares the root non-writable; every
    /// resource under the root is read-only by construction.
    /// </summary>
    ReadOnlyRoot,
}

/// <summary>
/// The workspace-scoped resource operation service. Layers session-local undo
/// and redo, batched grouping, and soft-delete trash on top of the IResourceFileSystem
/// gateway. Every method names its target with a ResourceKey; external
/// imports keep a string source path because the source lies outside the
/// registry by definition.
/// </summary>
public interface IResourceOperationService
{
    /// <summary>
    /// Creates a new file at the resource with the given content. Fails if the
    /// resource already exists.
    /// </summary>
    Task<Result> CreateFileAsync(ResourceKey resource, byte[] content);

    /// <summary>
    /// Creates a new empty folder at the resource, including any missing parents.
    /// Idempotent: succeeds if the folder already exists.
    /// </summary>
    Task<Result> CreateFolderAsync(ResourceKey resource);

    /// <summary>
    /// Copies a file or folder from one resource location to another. The
    /// returned CopyResult carries the paired-sidecar cascade outcome.
    /// </summary>
    Task<Result<CopyResult>> CopyAsync(ResourceKey source, ResourceKey dest);

    /// <summary>
    /// Moves a file or folder from one resource location to another. The
    /// returned MoveResult carries the reference-rewrite and paired-sidecar
    /// cascade outcomes.
    /// </summary>
    Task<Result<MoveResult>> MoveAsync(ResourceKey source, ResourceKey dest);

    /// <summary>
    /// Soft-deletes the resource via the trash service, preserving undo. The
    /// paired sidecar (if any) cascades into the same trash batch.
    /// </summary>
    Task<Result> DeleteAsync(ResourceKey resource);

    /// <summary>
    /// Imports a file from outside the project into a registry-addressable
    /// destination. The source path is taken as-is; the destination receives
    /// containment validation through the gateway.
    /// </summary>
    Task<Result> ImportExternalFileAsync(string sourcePath, ResourceKey dest);

    /// <summary>
    /// Imports a folder from outside the project into a registry-addressable
    /// destination. The source is enumerated recursively; each file lands at
    /// its corresponding destination key through the gateway.
    /// </summary>
    Task<Result> ImportExternalFolderAsync(string sourcePath, ResourceKey dest);

    /// <summary>
    /// Copies or moves the resource depending on the transfer mode. Dispatches
    /// file vs folder internally via the gateway's GetInfoAsync probe.
    /// </summary>
    Task<Result> TransferAsync(ResourceKey source, ResourceKey dest, DataTransferMode mode);

    /// <summary>
    /// Returns the writable state of the resource. When both sources apply, the
    /// priority order is ReadOnlyRoot > ReadOnlyAttribute.
    /// </summary>
    Task<WritableState> GetWritableStateAsync(ResourceKey resource);

    /// <summary>
    /// Read-only prediction of whether the resource can be deleted, renamed,
    /// moved, or cut. Mirrors the structural-change gate the executor enforces,
    /// including root writability. A failure carries the matched
    /// PolicyDenialError so the UI can name the rule.
    /// </summary>
    Task<Result> CanModifyResourceAsync(ResourceKey resource);

    /// <summary>
    /// Read-only prediction of whether a single resource can be created, copied,
    /// or moved to the destination key: root writability, List visibility, and
    /// Write lock. A failure carries the matched PolicyDenialError or a
    /// root-readonly reason. Used by drag-drop, paste, and inline dialog
    /// validation where the destination key is known.
    /// </summary>
    Result CanCreateResource(ResourceKey destination, bool isFolder);

    /// <summary>
    /// Read-only prediction of whether new resources can be added into the
    /// folder: root writability, folder visibility, and the folder not itself
    /// being write-locked. The precise per-name check still runs via
    /// CanCreateResource at create time. Used by menu state for the New File,
    /// New Folder, and Paste options where the child name is not yet known.
    /// </summary>
    Result CanAddToFolder(ResourceKey folder);

    /// <summary>
    /// Begins a batch of operations that commit as a single undo unit when the
    /// returned scope is disposed.
    /// </summary>
    IBatchScope BeginBatch();

    /// <summary>
    /// Returns true if there are operations that can be undone.
    /// </summary>
    bool CanUndo { get; }

    /// <summary>
    /// Returns true if there are operations that can be redone.
    /// </summary>
    bool CanRedo { get; }

    /// <summary>
    /// Undo the most recent operation or batch of operations.
    /// </summary>
    Task<Result> UndoAsync();

    /// <summary>
    /// Redo the most recently undone operation or batch of operations.
    /// </summary>
    Task<Result> RedoAsync();
}
