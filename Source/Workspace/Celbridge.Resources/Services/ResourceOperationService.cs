using Celbridge.DataTransfer;
using Celbridge.Entities;
using Celbridge.Logging;
using Celbridge.Resources.Helpers;
using Celbridge.Workspace;

namespace Celbridge.Resources.Services;

/// <summary>
/// Wraps the IFileStorage gateway and the ITrashService soft-delete layer
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

    private readonly IFileSystem _fileSystem;

    public ResourceOperationService(
        ILogger<ResourceOperationService> logger,
        IWorkspaceWrapper workspaceWrapper,
        IFileSystem fileSystem)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
        _fileSystem = fileSystem;
    }

    private IEntityService? EntityService =>
        _workspaceWrapper.IsWorkspacePageLoaded ? _workspaceWrapper.WorkspaceService.EntityService : null;

    private IResourceRegistry ResourceRegistry =>
        _workspaceWrapper.WorkspaceService.ResourceService.Registry;

    private IFileStorage FileStorage =>
        _workspaceWrapper.WorkspaceService.FileStorage;

    private ITrashService TrashService =>
        _workspaceWrapper.WorkspaceService.TrashService;

    public async Task<Result> CreateFileAsync(ResourceKey resource, byte[] content)
    {
        var operation = new CreateOperation(resource, content, FileStorage);
        var result = await operation.ExecuteAsync();

        if (result.IsSuccess)
        {
            AddOperation(operation);
        }

        return result;
    }

    public async Task<Result> CreateFolderAsync(ResourceKey resource)
    {
        var operation = new CreateOperation(resource, FileStorage);
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

        var infoResult = await FileStorage.GetInfoAsync(source);
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
            FileStorage);

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

        var infoResult = await FileStorage.GetInfoAsync(source);
        if (infoResult.IsFailure
            || infoResult.Value.Kind == StorageItemKind.NotFound)
        {
            return Result.Fail($"Source resource does not exist: '{source}'")
                .WithErrors(infoResult);
        }
        bool isFolder = infoResult.Value.Kind == StorageItemKind.Folder;

        var entityHelper = new EntityFileHelper(EntityService, ResourceRegistry);
        var operation = new MoveOperation(
            source,
            dest,
            isFolder,
            sourcePath,
            destPath,
            entityHelper,
            FileStorage);

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
        var operation = new DeleteOperation(resource, TrashService);
        var result = await operation.ExecuteAsync();

        if (result.IsSuccess)
        {
            AddOperation(operation);
        }

        return result;
    }

    public async Task<Result> ImportExternalFileAsync(string sourcePath, ResourceKey dest)
    {
        sourcePath = Path.GetFullPath(sourcePath);

        var operation = new ImportExternalOperation(sourcePath, dest, isFolder: false, FileStorage, _fileSystem, _logger);
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

        var operation = new ImportExternalOperation(sourcePath, dest, isFolder: true, FileStorage, _fileSystem, _logger);
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
