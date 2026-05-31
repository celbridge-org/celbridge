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
/// The workspace-scoped resource operation service. Layers session-local undo
/// and redo, batched grouping, and soft-delete trash on top of the IFileStorage
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
