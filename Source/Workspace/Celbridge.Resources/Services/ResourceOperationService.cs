using Celbridge.DataTransfer;
using Celbridge.Entities;
using Celbridge.Logging;
using Celbridge.Resources.Helpers;
using Celbridge.Workspace;

namespace Celbridge.Resources.Services;

/// <summary>
/// Wraps the IResourceFileSystem gateway and the ITrashService soft-delete layer
/// with a session-local undo/redo stack and batched grouping. Public methods
/// accept ResourceKey; external imports keep a string source path because the
/// source is outside the registry by definition. All disk I/O routes through
/// the gateway or the trash service; this class owns no direct System.IO
/// calls and no message broadcasts.
/// </summary>
public class ResourceOperationService : IResourceOperationService
{
    private const int MaxUndoStackSize = 50;

    private readonly ILogger<ResourceOperationService> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    private readonly List<FileOperation> _undoStack = new();
    private readonly List<FileOperation> _redoStack = new();

    private FileOperationBatch? _currentBatch;

    private readonly ILocalFileSystem _fileSystem;

    public ResourceOperationService(
        ILogger<ResourceOperationService> logger,
        IWorkspaceWrapper workspaceWrapper,
        ILocalFileSystem fileSystem)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
        _fileSystem = fileSystem;
    }

    private IEntityService? EntityService =>
        _workspaceWrapper.IsWorkspacePageLoaded ? _workspaceWrapper.WorkspaceService.EntityService : null;

    private IResourceRegistry ResourceRegistry =>
        _workspaceWrapper.WorkspaceService.ResourceService.Registry;

    private IResourceFileSystem ResourceFileSystem =>
        _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;

    private ITrashService TrashService =>
        _workspaceWrapper.WorkspaceService.ResourceService.Trash;

    public async Task<Result> CreateFileAsync(ResourceKey resource, byte[] content)
    {
        var operation = new CreateOperation(resource, content, ResourceFileSystem);
        var result = await operation.ExecuteAsync();

        if (result.IsSuccess)
        {
            AddOperation(operation);
        }

        return result;
    }

    public async Task<Result> CreateFolderAsync(ResourceKey resource)
    {
        var operation = new CreateOperation(resource, ResourceFileSystem);
        var result = await operation.ExecuteAsync();

        if (result.IsSuccess)
        {
            AddOperation(operation);
        }

        return result;
    }

    public async Task<Result<CopyResult>> CopyAsync(ResourceKey source, ResourceKey dest)
    {
        var sourcePathResult = ResourceRegistry.ResolveResourcePath(source);
        if (sourcePathResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for source resource: '{source}'")
                .WithErrors(sourcePathResult);
        }
        var sourcePath = sourcePathResult.Value;

        var destPathResult = ResourceRegistry.ResolveResourcePath(dest);
        if (destPathResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for destination resource: '{dest}'")
                .WithErrors(destPathResult);
        }
        var destPath = destPathResult.Value;

        var infoResult = await ResourceFileSystem.GetInfoAsync(source);
        if (infoResult.IsFailure
            || infoResult.Value.Kind == StorageItemKind.NotFound)
        {
            return Result.Fail($"Source resource does not exist: '{source}'")
                .WithErrors(infoResult);
        }
        bool isFolder = infoResult.Value.Kind == StorageItemKind.Folder;

        var entityHelper = new EntityFileHelper(EntityService, ResourceRegistry);
        var operation = new CopyOperation(
            source,
            dest,
            isFolder,
            sourcePath,
            destPath,
            entityHelper,
            ResourceFileSystem);

        var executeResult = await operation.ExecuteAsync();
        if (executeResult.IsFailure)
        {
            return Result.Fail(executeResult);
        }

        AddOperation(operation);

        return operation.LastCopyResult!;
    }

    public async Task<Result<MoveResult>> MoveAsync(ResourceKey source, ResourceKey dest)
    {
        var sourcePathResult = ResourceRegistry.ResolveResourcePath(source);
        if (sourcePathResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for source resource: '{source}'")
                .WithErrors(sourcePathResult);
        }
        var sourcePath = sourcePathResult.Value;

        var destPathResult = ResourceRegistry.ResolveResourcePath(dest);
        if (destPathResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for destination resource: '{dest}'")
                .WithErrors(destPathResult);
        }
        var destPath = destPathResult.Value;

        var infoResult = await ResourceFileSystem.GetInfoAsync(source);
        if (infoResult.IsFailure
            || infoResult.Value.Kind == StorageItemKind.NotFound)
        {
            return Result.Fail($"Source resource does not exist: '{source}'")
                .WithErrors(infoResult);
        }
        bool isFolder = infoResult.Value.Kind == StorageItemKind.Folder;

        // Moving or renaming a folder relocates every descendant, which changes
        // the path of any locked resource inside it. Walk the subtree and refuse
        // the move if any descendant is locked, freezing the locked resource's
        // path as well as its content.
        var policyGateResult = await EvaluateStructuralChangeAsync(source, isFolder);
        if (policyGateResult.IsFailure)
        {
            return Result.Fail(policyGateResult);
        }

        var entityHelper = new EntityFileHelper(EntityService, ResourceRegistry);
        var operation = new MoveOperation(
            source,
            dest,
            isFolder,
            sourcePath,
            destPath,
            entityHelper,
            ResourceFileSystem);

        var executeResult = await operation.ExecuteAsync();
        if (executeResult.IsFailure)
        {
            return Result.Fail(executeResult);
        }

        AddOperation(operation);

        return operation.LastMoveResult!;
    }

    public async Task<Result> DeleteAsync(ResourceKey resource)
    {
        // The soft-delete path bypasses IResourceFileSystem because TrashService
        // moves files into .celbridge/trash/ directly through the gateway. The
        // policy gate that lives on IResourceFileSystem.DeleteAsync would never
        // run, so the check is repeated here at the service entry. isFolder is
        // probed so folder-only locked patterns deny correctly.
        var infoResult = await ResourceFileSystem.GetInfoAsync(resource);
        bool isFolder = infoResult.IsSuccess
            && infoResult.Value.Kind == StorageItemKind.Folder;

        var policyGateResult = await EvaluateStructuralChangeAsync(resource, isFolder);
        if (policyGateResult.IsFailure)
        {
            return policyGateResult;
        }

        var operation = new DeleteOperation(resource, TrashService);
        var result = await operation.ExecuteAsync();

        if (result.IsSuccess)
        {
            AddOperation(operation);
        }

        return result;
    }

    // Evaluates the policy on a structural-change target (delete or move) and,
    // for folders, on every descendant. A locked descendant blocks the change
    // because delete moves the whole subtree to trash and move relocates it as
    // one unit; allowing the parent change while a child is locked would break
    // the locked resource's frozen-in-place guarantee.
    private async Task<Result> EvaluateStructuralChangeAsync(ResourceKey resource, bool isFolder)
    {
        var policy = _workspaceWrapper.WorkspaceService.ResourceService.Policy;

        var directResult = policy.Evaluate(resource, ResourceAction.Write, isFolder);
        if (directResult.IsFailure)
        {
            return Result.Fail(directResult);
        }

        if (!isFolder)
        {
            return Result.Ok();
        }

        var enumerateResult = await ResourceFileSystem.EnumerateFolderAsync(resource);
        if (enumerateResult.IsFailure)
        {
            return Result.Ok();
        }

        foreach (var entry in enumerateResult.Value)
        {
            var childResult = policy.Evaluate(entry.Resource, ResourceAction.Write, entry.IsFolder);
            if (childResult.IsFailure)
            {
                return Result.Fail(childResult);
            }

            if (entry.IsFolder)
            {
                var nestedResult = await EvaluateStructuralChangeAsync(entry.Resource, isFolder: true);
                if (nestedResult.IsFailure)
                {
                    return nestedResult;
                }
            }
        }

        return Result.Ok();
    }

    public async Task<Result> ImportExternalFileAsync(string sourcePath, ResourceKey dest)
    {
        sourcePath = Path.GetFullPath(sourcePath);

        var operation = new ImportExternalOperation(sourcePath, dest, isFolder: false, ResourceFileSystem, _fileSystem, _logger);
        var result = await operation.ExecuteAsync();

        if (result.IsSuccess)
        {
            AddOperation(operation);
        }

        return result;
    }

    public async Task<Result> ImportExternalFolderAsync(string sourcePath, ResourceKey dest)
    {
        sourcePath = Path.GetFullPath(sourcePath);

        var operation = new ImportExternalOperation(sourcePath, dest, isFolder: true, ResourceFileSystem, _fileSystem, _logger);
        var result = await operation.ExecuteAsync();

        if (result.IsSuccess)
        {
            AddOperation(operation);
        }

        return result;
    }

    public async Task<Result> TransferAsync(ResourceKey source, ResourceKey dest, DataTransferMode mode)
    {
        if (mode == DataTransferMode.Copy)
        {
            return await CopyAsync(source, dest);
        }

        return await MoveAsync(source, dest);
    }

    public IBatchScope BeginBatch()
    {
        if (_currentBatch != null)
        {
            _logger.LogWarning("BeginBatch called while a batch is already in progress");
            return new BatchScope(this, isOuter: false);
        }
        _currentBatch = new FileOperationBatch();
        return new BatchScope(this, isOuter: true);
    }

    // Empty batches are discarded; partial batches commit so the user can Ctrl+Z them.
    private void CommitBatch()
    {
        if (_currentBatch == null)
        {
            _logger.LogWarning("CommitBatch called without a batch in progress");
            return;
        }

        if (_currentBatch.Operations.Count > 0)
        {
            _undoStack.Add(_currentBatch);
            ClearRedoStack();
        }

        _currentBatch = null;
    }

    // Outer scope commits on dispose; a nested BeginBatch returns a no-op
    // scope that does nothing.
    private sealed class BatchScope : IBatchScope
    {
        private readonly ResourceOperationService _owner;
        private readonly bool _isOuter;
        private bool _disposed;

        public BatchScope(ResourceOperationService owner, bool isOuter)
        {
            _owner = owner;
            _isOuter = isOuter;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            if (_isOuter)
            {
                _owner.CommitBatch();
            }
        }
    }

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public async Task<Result> UndoAsync()
    {
        if (_undoStack.Count == 0)
        {
            return Result.Ok();
        }

        var operation = _undoStack[^1];

        // Run the undo against the in-place operation; only move it between
        // stacks once the outcome is known. A failed undo leaves the operation
        // on the undo stack so the user can retry once the underlying issue
        // clears (file unlocked, permission granted), instead of losing the
        // entry to the abyss.
        var result = await operation.UndoAsync();
        if (result.IsSuccess)
        {
            _undoStack.RemoveAt(_undoStack.Count - 1);
            _redoStack.Add(operation);
        }
        else
        {
            _logger.LogError(result, "Failed to undo file operation");
        }

        return result;
    }

    public async Task<Result> RedoAsync()
    {
        if (_redoStack.Count == 0)
        {
            return Result.Ok();
        }

        var operation = _redoStack[^1];

        // Mirror of UndoAsync: only move the operation between stacks on
        // success. A failed redo stays on the redo stack so the user can retry.
        var result = await operation.RedoAsync();
        if (result.IsSuccess)
        {
            _redoStack.RemoveAt(_redoStack.Count - 1);
            _undoStack.Add(operation);
        }
        else
        {
            _logger.LogError(result, "Failed to redo file operation");
        }

        return result;
    }

    private void AddOperation(FileOperation operation)
    {
        if (_currentBatch != null)
        {
            _currentBatch.Operations.Add(operation);
        }
        else
        {
            _undoStack.Add(operation);
            ClearRedoStack();
            TrimUndoStack();
        }
    }

    // Drop oldest operations once the stack reaches MaxUndoStackSize and purge
    // any trash bytes they were keeping alive for undo.
    private void TrimUndoStack()
    {
        while (_undoStack.Count > MaxUndoStackSize)
        {
            var oldestOperation = _undoStack[0];
            _undoStack.RemoveAt(0);

            FireAndForgetPurge(oldestOperation);
        }
    }

    // The redo stack is invalidated whenever a new operation lands. Purge any
    // trash bytes the cleared redo entries were holding open.
    private void ClearRedoStack()
    {
        foreach (var operation in _redoStack)
        {
            FireAndForgetPurge(operation);
        }
        _redoStack.Clear();
    }

    // Schedules a best-effort purge of the operation's trash bytes without
    // blocking the caller. The wrapper guarantees that any exception thrown
    // inside the chain is logged rather than escaping as an unobserved task
    // exception — internal purge already logs at Warning, but a programming
    // error introduced later in the chain would otherwise be invisible.
    private void FireAndForgetPurge(FileOperation operation)
    {
        _ = PurgeOperationTrashAsync(operation).ContinueWith(task =>
        {
            if (task.Exception is not null)
            {
                _logger.LogWarning(task.Exception, "Unhandled exception while purging operation trash.");
            }
        }, TaskScheduler.Default);
    }

    // Recursively walks operation batches and purges any trash bytes a
    // DeleteOperation was keeping alive. Called via FireAndForgetPurge at the
    // call site because trash purge is best-effort cleanup.
    private static async Task PurgeOperationTrashAsync(FileOperation operation)
    {
        if (operation is FileOperationBatch batch)
        {
            foreach (var inner in batch.Operations)
            {
                await PurgeOperationTrashAsync(inner);
            }
        }
        else if (operation is DeleteOperation delete)
        {
            await delete.CleanupAsync();
        }
    }

}
