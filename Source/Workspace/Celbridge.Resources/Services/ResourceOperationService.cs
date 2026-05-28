using Celbridge.DataTransfer;
using Celbridge.Entities;
using Celbridge.Logging;
using Celbridge.Resources.Helpers;
using Celbridge.Workspace;

namespace Celbridge.Resources.Services;

/// <summary>
/// Wraps the IFileStorage chokepoint and the ITrashService soft-delete layer
/// with a session-local undo/redo stack and batched grouping. Public methods
/// accept ResourceKey; external imports keep a string source path because the
/// source is outside the registry by definition. All actual disk I/O routes
/// through the chokepoint or the trash service; this class owns no direct
/// System.IO calls.
/// </summary>
public class ResourceOperationService : IResourceOperationService
{
    private const int MaxUndoStackSize = 50;

    private readonly ILogger<ResourceOperationService> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    private readonly List<FileOperation> _undoStack = new();
    private readonly List<FileOperation> _redoStack = new();

    private FileOperationBatch? _currentBatch;

    public ResourceOperationService(
        ILogger<ResourceOperationService> logger,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;
    }

    // Default outcomes returned when the chokepoint's cascade did not run
    // (typically because the source is outside the project tree).
    private static readonly CopyResult EmptyCopyResult = new(SidecarOutcome.NotPresent);
    private static readonly MoveResult EmptyMoveResult = new(
        Array.Empty<ResourceKey>(),
        Array.Empty<SkippedReferencer>(),
        SidecarOutcome.NotPresent);

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

    public async Task<Result<CopyResult>> CopyAsync(ResourceKey source, ResourceKey destination)
    {
        var sourcePathResult = ResourceRegistry.ResolveResourcePath(source);
        if (sourcePathResult.IsFailure)
        {
            return Result<CopyResult>.Fail($"Failed to resolve path for source resource: '{source}'")
                .WithErrors(sourcePathResult);
        }
        var sourcePath = sourcePathResult.Value;

        var destinationPathResult = ResourceRegistry.ResolveResourcePath(destination);
        if (destinationPathResult.IsFailure)
        {
            return Result<CopyResult>.Fail($"Failed to resolve path for destination resource: '{destination}'")
                .WithErrors(destinationPathResult);
        }
        var destinationPath = destinationPathResult.Value;

        var infoResult = await FileStorage.GetInfoAsync(source);
        if (infoResult.IsFailure
            || infoResult.Value.Kind == StorageItemKind.NotFound)
        {
            return Result<CopyResult>.Fail($"Source resource does not exist: '{source}'");
        }
        bool isFolder = infoResult.Value.Kind == StorageItemKind.Folder;

        var entityHelper = new EntityFileHelper(EntityService, ResourceRegistry);
        var operation = new CopyOperation(
            source,
            destination,
            isFolder,
            sourcePath,
            destinationPath,
            entityHelper,
            FileStorage);

        var executeResult = await operation.ExecuteAsync();
        if (executeResult.IsFailure)
        {
            return Result<CopyResult>.Fail(executeResult);
        }

        AddOperation(operation);
        return operation.LastCopyResult ?? EmptyCopyResult;
    }

    public async Task<Result<MoveResult>> MoveAsync(ResourceKey source, ResourceKey destination)
    {
        var sourcePathResult = ResourceRegistry.ResolveResourcePath(source);
        if (sourcePathResult.IsFailure)
        {
            return Result<MoveResult>.Fail($"Failed to resolve path for source resource: '{source}'")
                .WithErrors(sourcePathResult);
        }
        var sourcePath = sourcePathResult.Value;

        var destinationPathResult = ResourceRegistry.ResolveResourcePath(destination);
        if (destinationPathResult.IsFailure)
        {
            return Result<MoveResult>.Fail($"Failed to resolve path for destination resource: '{destination}'")
                .WithErrors(destinationPathResult);
        }
        var destinationPath = destinationPathResult.Value;

        var infoResult = await FileStorage.GetInfoAsync(source);
        if (infoResult.IsFailure
            || infoResult.Value.Kind == StorageItemKind.NotFound)
        {
            return Result<MoveResult>.Fail($"Source resource does not exist: '{source}'");
        }
        bool isFolder = infoResult.Value.Kind == StorageItemKind.Folder;

        var entityHelper = new EntityFileHelper(EntityService, ResourceRegistry);
        var operation = new MoveOperation(
            source,
            destination,
            isFolder,
            sourcePath,
            destinationPath,
            entityHelper,
            FileStorage);

        var executeResult = await operation.ExecuteAsync();
        if (executeResult.IsFailure)
        {
            return Result<MoveResult>.Fail(executeResult);
        }

        AddOperation(operation);

        if (isFolder)
        {
            SendFolderResourceKeyChangedMessages(source, destination);
        }
        else
        {
            var message = new ResourceKeyChangedMessage(source, destination);
            _messengerService.Send(message);
        }

        return Result<MoveResult>.Ok(operation.LastMoveResult ?? EmptyMoveResult);
    }

    public async Task<Result> DeleteAsync(ResourceKey resource)
    {
        var operation = new DeleteOperation(resource, TrashService);
        var result = await operation.ExecuteAsync();

        if (result.IsSuccess)
        {
            AddOperation(operation);
            BroadcastDeleteMessages(operation.TrashEntry);
        }

        return result;
    }

    public async Task<Result> ImportExternalFileAsync(string sourcePath, ResourceKey destination)
    {
        sourcePath = Path.GetFullPath(sourcePath);

        var operation = new ImportExternalOperation(sourcePath, destination, isFolder: false, FileStorage);
        var result = await operation.ExecuteAsync();

        if (result.IsSuccess)
        {
            AddOperation(operation);
        }

        return result;
    }

    public async Task<Result> ImportExternalFolderAsync(string sourcePath, ResourceKey destination)
    {
        sourcePath = Path.GetFullPath(sourcePath);

        var operation = new ImportExternalOperation(sourcePath, destination, isFolder: true, FileStorage);
        var result = await operation.ExecuteAsync();

        if (result.IsSuccess)
        {
            AddOperation(operation);
        }

        return result;
    }

    public async Task<Result> TransferAsync(ResourceKey source, ResourceKey destination, DataTransferMode mode)
    {
        var infoResult = await FileStorage.GetInfoAsync(source);
        if (infoResult.IsFailure
            || infoResult.Value.Kind == StorageItemKind.NotFound)
        {
            return Result.Fail($"Source resource does not exist: '{source}'");
        }

        if (mode == DataTransferMode.Copy)
        {
            return await CopyAsync(source, destination);
        }

        return await MoveAsync(source, destination);
    }

    public void BeginBatch()
    {
        if (_currentBatch != null)
        {
            _logger.LogWarning("BeginBatch called while a batch is already in progress");
            return;
        }
        _currentBatch = new FileOperationBatch();
    }

    public void CommitBatch()
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

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public async Task<Result> UndoAsync()
    {
        if (_undoStack.Count == 0)
        {
            return Result.Ok();
        }

        var operation = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);

        var result = await operation.UndoAsync();
        if (result.IsSuccess)
        {
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
        _redoStack.RemoveAt(_redoStack.Count - 1);

        var result = await operation.RedoAsync();
        if (result.IsSuccess)
        {
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

            _ = PurgeOperationTrashAsync(oldestOperation);
        }
    }

    // The redo stack is invalidated whenever a new operation lands. Purge any
    // trash bytes the cleared redo entries were holding open.
    private void ClearRedoStack()
    {
        foreach (var operation in _redoStack)
        {
            _ = PurgeOperationTrashAsync(operation);
        }
        _redoStack.Clear();
    }

    // Recursively walks operation batches and purges any trash bytes a
    // DeleteOperation was keeping alive. Fire-and-forget at the call site
    // because trash purge is best-effort cleanup.
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

    private void BroadcastDeleteMessages(TrashEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        var sourceRemovedMessage = new ResourceDeletedMessage(entry.OriginalResource);
        _messengerService.Send(sourceRemovedMessage);

        foreach (var descendant in entry.DescendantKeys)
        {
            var descendantRemovedMessage = new ResourceDeletedMessage(descendant);
            _messengerService.Send(descendantRemovedMessage);
        }
    }

    // After a folder move, broadcast a key-changed message for the folder and
    // every descendant resource so opened documents can repoint cleanly. Walks
    // the registry-cached source tree because the on-disk source is already
    // gone by the time we get here.
    private void SendFolderResourceKeyChangedMessages(ResourceKey sourceFolder, ResourceKey destinationFolder)
    {
        var folderMessage = new ResourceKeyChangedMessage(sourceFolder, destinationFolder);
        _messengerService.Send(folderMessage);

        var getResourceResult = ResourceRegistry.GetResource(sourceFolder);
        if (getResourceResult.IsFailure)
        {
            return;
        }

        if (getResourceResult.Value is not FolderResource sourceFolderResource)
        {
            return;
        }

        var sourceResources = new List<ResourceKey>();
        PopulateSourceResources(sourceFolderResource);

        void PopulateSourceResources(FolderResource folderResource)
        {
            foreach (var childResource in folderResource.Children)
            {
                if (childResource is FolderResource childFolderResource)
                {
                    var folderKey = ResourceRegistry.GetResourceKey(childFolderResource);
                    sourceResources.Add(folderKey);
                    PopulateSourceResources(childFolderResource);
                }
                else
                {
                    var fileKey = ResourceRegistry.GetResourceKey(childResource);
                    sourceResources.Add(fileKey);
                }
            }
        }

        foreach (var descendantSource in sourceResources)
        {
            var descendantDestination = descendantSource.ToString().Replace(sourceFolder, destinationFolder);
            var message = new ResourceKeyChangedMessage(descendantSource, descendantDestination);
            _messengerService.Send(message);
        }
    }
}
