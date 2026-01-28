using Celbridge.DataTransfer;
using Celbridge.Entities;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Workspace;

namespace Celbridge.Resources.Services;

/// <summary>
/// Service for performing file system operations with undo/redo support.
/// Uses a soft-delete trash folder approach for delete operations.
/// </summary>
public class FileOperationService : IFileOperationService
{
    private const int MaxUndoStackSize = 50;

    private readonly ILogger<FileOperationService> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    private readonly List<FileOperation> _undoStack = new();
    private readonly List<FileOperation> _redoStack = new();

    private FileOperationBatch? _currentBatch;

    public FileOperationService(
        ILogger<FileOperationService> logger,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;
    }

    private IEntityService? EntityService =>
        _workspaceWrapper.IsWorkspacePageLoaded ? _workspaceWrapper.WorkspaceService.EntityService : null;

    private IResourceRegistry? ResourceRegistry =>
        _workspaceWrapper.IsWorkspacePageLoaded ? _workspaceWrapper.WorkspaceService.ResourceService.Registry : null;

    private string ProjectFolderPath =>
        _workspaceWrapper.IsWorkspacePageLoaded ? ResourceRegistry!.ProjectFolderPath : string.Empty;

    /// <summary>
    /// Gets the path to the trash folder for soft-deleted files.
    /// </summary>
    private string TrashFolderPath =>
        Path.Combine(ProjectFolderPath, ProjectConstants.MetaDataFolder, ProjectConstants.TrashFolder);

    public async Task<Result> CreateFileAsync(string path, byte[] content)
    {
        path = Path.GetFullPath(path);

        var operation = new CreateFileOperation(path, content);
        var result = await operation.ExecuteAsync();

        if (result.IsSuccess)
        {
            AddOperation(operation);
        }

        return result;
    }

    public async Task<Result> CreateFolderAsync(string path)
    {
        path = Path.GetFullPath(path);

        var operation = new CreateFolderOperation(path);
        var result = await operation.ExecuteAsync();

        if (result.IsSuccess)
        {
            AddOperation(operation);
        }

        return result;
    }

    public async Task<Result> CopyFileAsync(string sourcePath, string destPath)
    {
        sourcePath = Path.GetFullPath(sourcePath);
        destPath = Path.GetFullPath(destPath);

        var operation = new CopyFileOperation(sourcePath, destPath, EntityService, ResourceRegistry);
        var result = await operation.ExecuteAsync();

        if (result.IsSuccess)
        {
            AddOperation(operation);
        }

        return result;
    }

    public async Task<Result> MoveFileAsync(string sourcePath, string destPath)
    {
        sourcePath = Path.GetFullPath(sourcePath);
        destPath = Path.GetFullPath(destPath);

        var operation = new MoveFileOperation(sourcePath, destPath, EntityService, ResourceRegistry);
        var result = await operation.ExecuteAsync();

        if (result.IsSuccess)
        {
            AddOperation(operation);

            // Notify opened documents that the file has moved
            SendResourceKeyChangedMessage(sourcePath, destPath);
        }

        return result;
    }

    public async Task<Result> DeleteFileAsync(string path)
    {
        path = Path.GetFullPath(path);

        if (!File.Exists(path))
        {
            return Result.Fail($"File does not exist: {path}");
        }

        // Pre-compute trash paths
        var trashId = Guid.NewGuid().ToString();
        var relativePath = Path.GetRelativePath(ProjectFolderPath, path);
        var trashPath = Path.Combine(TrashFolderPath, trashId, relativePath);

        // Pre-compute entity data paths
        string? entityDataPath = null;
        string? entityDataTrashPath = null;
        if (EntityService != null && ResourceRegistry != null)
        {
            var resourceKeyResult = ResourceRegistry.GetResourceKey(path);
            if (resourceKeyResult.IsSuccess)
            {
                var existingEntityDataPath = EntityService.GetEntityDataPath(resourceKeyResult.Value);
                if (File.Exists(existingEntityDataPath))
                {
                    entityDataPath = existingEntityDataPath;
                    var entityDataRelativePath = EntityService.GetEntityDataRelativePath(resourceKeyResult.Value);
                    entityDataTrashPath = Path.Combine(TrashFolderPath, trashId, entityDataRelativePath);
                }
            }
        }

        var operation = new DeleteFileOperation(path, trashPath, entityDataPath, entityDataTrashPath);
        var result = await operation.ExecuteAsync();

        if (result.IsSuccess)
        {
            AddOperation(operation);
        }

        return result;
    }

    public async Task<Result> CopyFolderAsync(string sourcePath, string destPath)
    {
        sourcePath = Path.GetFullPath(sourcePath);
        destPath = Path.GetFullPath(destPath);

        var operation = new CopyFolderOperation(sourcePath, destPath, EntityService, ResourceRegistry);
        var result = await operation.ExecuteAsync();

        if (result.IsSuccess)
        {
            AddOperation(operation);
        }

        return result;
    }

    public async Task<Result> MoveFolderAsync(string sourcePath, string destPath)
    {
        sourcePath = Path.GetFullPath(sourcePath);
        destPath = Path.GetFullPath(destPath);

        var operation = new MoveFolderOperation(sourcePath, destPath, EntityService, ResourceRegistry);
        var result = await operation.ExecuteAsync();

        if (result.IsSuccess)
        {
            AddOperation(operation);

            // Notify opened documents that resources in this folder have moved
            SendFolderResourceKeyChangedMessages(sourcePath, destPath);
        }

        return result;
    }

    public async Task<Result> DeleteFolderAsync(string path)
    {
        path = Path.GetFullPath(path);

        if (!Directory.Exists(path))
        {
            return Result.Fail($"Folder does not exist: {path}");
        }

        var files = Directory.GetFiles(path);
        var directories = Directory.GetDirectories(path);
        var wasEmpty = files.Length == 0 && directories.Length == 0;

        // Pre-compute trash paths
        var trashId = Guid.NewGuid().ToString();
        var relativePath = Path.GetRelativePath(ProjectFolderPath, path);
        var trashPath = wasEmpty ? string.Empty : Path.Combine(TrashFolderPath, trashId, relativePath);

        // Pre-compute entity data file paths for trash
        var entityDataFiles = new List<(string OriginalPath, string TrashPath)>();
        if (!wasEmpty && EntityService != null && ResourceRegistry != null)
        {
            var trashBasePath = Path.Combine(TrashFolderPath, trashId);
            foreach (var filePath in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                var resourceKeyResult = ResourceRegistry.GetResourceKey(filePath);
                if (resourceKeyResult.IsFailure)
                {
                    continue;
                }

                var entityDataPath = EntityService.GetEntityDataPath(resourceKeyResult.Value);
                if (!File.Exists(entityDataPath))
                {
                    continue;
                }

                var entityDataRelativePath = EntityService.GetEntityDataRelativePath(resourceKeyResult.Value);
                var entityDataTrashPath = Path.Combine(trashBasePath, entityDataRelativePath);
                entityDataFiles.Add((entityDataPath, entityDataTrashPath));
            }
        }

        var operation = new DeleteFolderOperation(path, trashPath, wasEmpty, entityDataFiles);
        var result = await operation.ExecuteAsync();

        if (result.IsSuccess)
        {
            AddOperation(operation);
        }

        return result;
    }

    public async Task<Result> TransferAsync(string sourcePath, string destPath, DataTransferMode mode)
    {
        sourcePath = Path.GetFullPath(sourcePath);

        bool isFile = File.Exists(sourcePath);
        bool isFolder = Directory.Exists(sourcePath);

        if (!isFile && !isFolder)
        {
            return Result.Fail($"Source does not exist: {sourcePath}");
        }

        if (isFile)
        {
            return mode == DataTransferMode.Copy
                ? await CopyFileAsync(sourcePath, destPath)
                : await MoveFileAsync(sourcePath, destPath);
        }
        else
        {
            return mode == DataTransferMode.Copy
                ? await CopyFolderAsync(sourcePath, destPath)
                : await MoveFolderAsync(sourcePath, destPath);
        }
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
            return Result.Ok(); // Nothing to undo
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
            return Result.Ok(); // Nothing to redo
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

            // Enforce undo stack size limit
            TrimUndoStack();
        }
    }

    /// <summary>
    /// Remove oldest operations from the undo stack if it exceeds the maximum size.
    /// Also cleans up any associated trash files for removed delete operations.
    /// </summary>
    private void TrimUndoStack()
    {
        while (_undoStack.Count > MaxUndoStackSize)
        {
            var oldestOperation = _undoStack[0];
            _undoStack.RemoveAt(0);

            // Clean up trash files for delete operations that are being discarded
            CleanupOperationTrashFiles(oldestOperation);
        }
    }

    /// <summary>
    /// Clear the redo stack and clean up any associated trash files.
    /// This is called when a new operation is performed, invalidating the redo history.
    /// </summary>
    private void ClearRedoStack()
    {
        foreach (var operation in _redoStack)
        {
            CleanupOperationTrashFiles(operation);
        }
        _redoStack.Clear();
    }

    /// <summary>
    /// Recursively clean up trash files associated with an operation.
    /// </summary>
    private void CleanupOperationTrashFiles(FileOperation operation)
    {
        if (operation is FileOperationBatch batch)
        {
            foreach (var op in batch.Operations)
            {
                CleanupOperationTrashFiles(op);
            }
        }
        else if (operation is DeleteFileOperation deleteFile)
        {
            deleteFile.CleanupTrashFile();
        }
        else if (operation is DeleteFolderOperation deleteFolder)
        {
            deleteFolder.CleanupTrashFolder();
        }
    }

    /// <summary>
    /// Send a ResourceKeyChangedMessage for a single file that has been moved.
    /// </summary>
    private void SendResourceKeyChangedMessage(string sourcePath, string destPath)
    {
        if (ResourceRegistry == null)
        {
            return;
        }

        var sourceKeyResult = ResourceRegistry.GetResourceKey(sourcePath);
        var destKeyResult = ResourceRegistry.GetResourceKey(destPath);

        if (sourceKeyResult.IsSuccess && destKeyResult.IsSuccess)
        {
            var message = new ResourceKeyChangedMessage(sourceKeyResult.Value, destKeyResult.Value);
            _messengerService.Send(message);
        }
    }

    /// <summary>
    /// Send ResourceKeyChangedMessage for all resources in a folder that has been moved.
    /// </summary>
    private void SendFolderResourceKeyChangedMessages(string sourceFolderPath, string destFolderPath)
    {
        if (ResourceRegistry == null)
        {
            return;
        }

        var sourceKeyResult = ResourceRegistry.GetResourceKey(sourceFolderPath);
        var destKeyResult = ResourceRegistry.GetResourceKey(destFolderPath);

        if (sourceKeyResult.IsFailure || destKeyResult.IsFailure)
        {
            return;
        }

        var sourceFolder = sourceKeyResult.Value;
        var destFolder = destKeyResult.Value;

        var getResourceResult = ResourceRegistry.GetResource(sourceFolder);
        if (getResourceResult.IsFailure)
        {
            return;
        }

        if (getResourceResult.Value is not FolderResource sourceFolderResource)
        {
            return;
        }

        List<ResourceKey> sourceResources = new();
        PopulateSourceResources(sourceFolderResource);

        void PopulateSourceResources(FolderResource folderResource)
        {
            var folderKey = ResourceRegistry.GetResourceKey(folderResource);
            sourceResources.Add(folderKey);

            foreach (var childResource in folderResource.Children)
            {
                if (childResource is FolderResource childFolderResource)
                {
                    PopulateSourceResources(childFolderResource);
                }
                else
                {
                    var fileKey = ResourceRegistry.GetResourceKey(childResource);
                    sourceResources.Add(fileKey);
                }
            }
        }

        foreach (var sourceResource in sourceResources)
        {
            var destResource = sourceResource.ToString().Replace(sourceFolder, destFolder);
            var message = new ResourceKeyChangedMessage(sourceResource, destResource);
            _messengerService.Send(message);
        }
    }
}
