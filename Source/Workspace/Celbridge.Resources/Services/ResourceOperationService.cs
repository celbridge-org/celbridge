using Celbridge.DataTransfer;
using Celbridge.Entities;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Resources.Helpers;
using Celbridge.Workspace;

namespace Celbridge.Resources.Services;

/// <summary>
/// Service for performing resource operations with undo/redo support.
/// Uses a soft-delete trash folder approach for delete operations.
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

    private IEntityService? EntityService =>
        _workspaceWrapper.IsWorkspacePageLoaded ? _workspaceWrapper.WorkspaceService.EntityService : null;

    private IResourceRegistry? ResourceRegistry =>
        _workspaceWrapper.IsWorkspacePageLoaded ? _workspaceWrapper.WorkspaceService.ResourceService.Registry : null;

    private IResourceFileSystem? FileSystem =>
        _workspaceWrapper.IsWorkspacePageLoaded ? _workspaceWrapper.WorkspaceService.ResourceFileSystem : null;

    private string ProjectFolderPath =>
        _workspaceWrapper.IsWorkspacePageLoaded ? ResourceRegistry!.ProjectFolderPath : string.Empty;

    /// <summary>
    /// Gets the path to the trash folder for soft-deleted files.
    /// </summary>
    private string TrashFolderPath =>
        Path.Combine(ProjectFolderPath, ProjectConstants.CelbridgeFolder, ProjectConstants.CelbridgeTrashFolder);

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

    // Default outcome returned when the FS-layer cascade did not run (e.g.
    // external import via the path-based fallback). Callers treating the empty
    // structure as "no cascade work was applicable" stay symmetric with the
    // real-cascade case.
    private static readonly CopyResult EmptyCopyResult = new(SidecarOutcome.NotPresent);
    private static readonly MoveResult EmptyMoveResult = new(
        Array.Empty<ResourceKey>(),
        Array.Empty<SkippedReferencer>(),
        SidecarOutcome.NotPresent);

    public async Task<Result<CopyResult>> CopyFileAsync(string sourcePath, string destPath)
    {
        sourcePath = Path.GetFullPath(sourcePath);
        destPath = Path.GetFullPath(destPath);

        // External-import callers (TransferResourcesCommand.AddExternalResourceAsync,
        // AddResourceHelper) supply a source path outside the project folder. The
        // FS-layer cascade does not apply: the implicit "<source>.cel" sidecar
        // lookup is rooted in resource keys and can't address external paths, and
        // external bytes have no inbound references in this project. Sidecars that
        // are explicitly selected (file-by-file) or contained in a copied folder
        // come along as ordinary bytes via the path-based fallback; the registry's
        // pairing pass picks them up on the next sync. Stale "project:" references
        // inside imported sidecar bodies surface via metadata_check_project (ri-2).
        if (!IsInProjectFolder(sourcePath))
        {
            return await CopyExternalFileAsync(sourcePath, destPath);
        }

        var keyResult = ResolveOperationKeys(sourcePath, destPath);
        if (keyResult.IsFailure)
        {
            return Result.Fail(keyResult);
        }
        var fileSystem = FileSystem;
        if (fileSystem is null)
        {
            return Result<CopyResult>.Fail("Workspace is not loaded; resource file system is unavailable.");
        }

        var operation = new CopyFileOperation(
            sourcePath,
            destPath,
            keyResult.Value.Source,
            keyResult.Value.Destination,
            EntityService,
            ResourceRegistry,
            fileSystem);
        var execResult = await operation.ExecuteAsync();

        if (execResult.IsFailure)
        {
            return Result.Fail(execResult);
        }

        AddOperation(operation);
        return Result<CopyResult>.Ok(operation.LastCopyResult ?? EmptyCopyResult);
    }

    private async Task<Result<CopyResult>> CopyExternalFileAsync(string sourcePath, string destPath)
    {
        var operation = new CopyExternalFileOperation(sourcePath, destPath);
        var execResult = await operation.ExecuteAsync();
        if (execResult.IsFailure)
        {
            return Result.Fail(execResult);
        }
        AddOperation(operation);
        return Result<CopyResult>.Ok(EmptyCopyResult);
    }

    private async Task<Result<CopyResult>> CopyExternalFolderAsync(string sourcePath, string destPath)
    {
        var operation = new CopyExternalFolderOperation(sourcePath, destPath);
        var execResult = await operation.ExecuteAsync();
        if (execResult.IsFailure)
        {
            return Result.Fail(execResult);
        }
        AddOperation(operation);
        return Result<CopyResult>.Ok(EmptyCopyResult);
    }

    private bool IsInProjectFolder(string absolutePath)
    {
        var projectFolderPath = ProjectFolderPath;
        if (string.IsNullOrEmpty(projectFolderPath))
        {
            return false;
        }

        var normalizedProject = Path.GetFullPath(projectFolderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedPath = Path.GetFullPath(absolutePath);
        return normalizedPath.StartsWith(normalizedProject + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedPath, normalizedProject, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<Result<MoveResult>> MoveFileAsync(string sourcePath, string destPath)
    {
        sourcePath = Path.GetFullPath(sourcePath);
        destPath = Path.GetFullPath(destPath);

        var keyResult = ResolveOperationKeys(sourcePath, destPath);
        if (keyResult.IsFailure)
        {
            return Result.Fail(keyResult);
        }
        var fileSystem = FileSystem;
        if (fileSystem is null)
        {
            return Result<MoveResult>.Fail("Workspace is not loaded; resource file system is unavailable.");
        }

        var operation = new MoveFileOperation(
            sourcePath,
            destPath,
            keyResult.Value.Source,
            keyResult.Value.Destination,
            EntityService,
            ResourceRegistry,
            fileSystem);
        var execResult = await operation.ExecuteAsync();

        if (execResult.IsFailure)
        {
            return Result.Fail(execResult);
        }

        AddOperation(operation);

        // Notify opened documents that the file has moved
        SendResourceKeyChangedMessage(sourcePath, destPath);

        return Result<MoveResult>.Ok(operation.LastMoveResult ?? EmptyMoveResult);
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

        // Pre-compute sidecar paths so the cascade can land in the same trash
        // batch as the parent file. The sibling lookup is a pure filename check
        // (matches the FS-layer cascade rule); it does not consult the registry.
        string? sidecarPath = null;
        string? sidecarTrashPath = null;
        var siblingSidecar = path + SidecarHelper.Extension;
        if (File.Exists(siblingSidecar))
        {
            sidecarPath = siblingSidecar;
            sidecarTrashPath = trashPath + SidecarHelper.Extension;
        }

        var operation = new DeleteFileOperation(path, trashPath, entityDataPath, entityDataTrashPath, sidecarPath, sidecarTrashPath);
        var result = await operation.ExecuteAsync();

        if (result.IsSuccess)
        {
            AddOperation(operation);

            // Announce the removal synchronously so subscribers update before
            // control returns. The watcher's own delete event still arrives
            // later via UI-thread dispatch; subscribers must treat these
            // messages as idempotent.
            if (ResourceRegistry is not null)
            {
                var keyResult = ResourceRegistry.GetResourceKey(path);
                if (keyResult.IsSuccess)
                {
                    var removedMessage = new ResourceDeletedMessage(keyResult.Value);
                    _messengerService.Send(removedMessage);
                }
            }
        }

        return result;
    }

    public async Task<Result<CopyResult>> CopyFolderAsync(string sourcePath, string destPath)
    {
        sourcePath = Path.GetFullPath(sourcePath);
        destPath = Path.GetFullPath(destPath);

        // External-import callers supply a source folder outside the project.
        // The FS-layer cascade does not apply (see CopyFileAsync for the full
        // rationale). Sidecars inside the source folder come along as ordinary
        // bytes via the recursive copy; the registry pairing pass picks them up.
        if (!IsInProjectFolder(sourcePath))
        {
            return await CopyExternalFolderAsync(sourcePath, destPath);
        }

        var keyResult = ResolveOperationKeys(sourcePath, destPath);
        if (keyResult.IsFailure)
        {
            return Result.Fail(keyResult);
        }
        var fileSystem = FileSystem;
        if (fileSystem is null)
        {
            return Result<CopyResult>.Fail("Workspace is not loaded; resource file system is unavailable.");
        }

        var operation = new CopyFolderOperation(
            sourcePath,
            destPath,
            keyResult.Value.Source,
            keyResult.Value.Destination,
            EntityService,
            ResourceRegistry,
            fileSystem);
        var execResult = await operation.ExecuteAsync();

        if (execResult.IsFailure)
        {
            return Result.Fail(execResult);
        }

        AddOperation(operation);
        return Result<CopyResult>.Ok(operation.LastCopyResult ?? EmptyCopyResult);
    }

    public async Task<Result<MoveResult>> MoveFolderAsync(string sourcePath, string destPath)
    {
        sourcePath = Path.GetFullPath(sourcePath);
        destPath = Path.GetFullPath(destPath);

        var keyResult = ResolveOperationKeys(sourcePath, destPath);
        if (keyResult.IsFailure)
        {
            return Result.Fail(keyResult);
        }
        var fileSystem = FileSystem;
        if (fileSystem is null)
        {
            return Result<MoveResult>.Fail("Workspace is not loaded; resource file system is unavailable.");
        }

        var operation = new MoveFolderOperation(
            sourcePath,
            destPath,
            keyResult.Value.Source,
            keyResult.Value.Destination,
            EntityService,
            ResourceRegistry,
            fileSystem);
        var execResult = await operation.ExecuteAsync();

        if (execResult.IsFailure)
        {
            return Result.Fail(execResult);
        }

        AddOperation(operation);

        // Notify opened documents that resources in this folder have moved
        SendFolderResourceKeyChangedMessages(sourcePath, destPath);

        return Result<MoveResult>.Ok(operation.LastMoveResult ?? EmptyMoveResult);
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

        // Capture descendant keys (folders only) before the disk delete so the
        // post-delete eager-notify can drop their stale entries too.
        var descendantKeys = new List<ResourceKey>();
        if (!wasEmpty && ResourceRegistry is not null)
        {
            foreach (var filePath in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                var keyResult = ResourceRegistry.GetResourceKey(filePath);
                if (keyResult.IsSuccess)
                {
                    descendantKeys.Add(keyResult.Value);
                }
            }
        }

        var operation = new DeleteFolderOperation(path, trashPath, wasEmpty, entityDataFiles);
        var result = await operation.ExecuteAsync();

        if (result.IsSuccess)
        {
            AddOperation(operation);

            // Announce the removal synchronously so subscribers update before
            // control returns. The folder key and every captured descendant are
            // broadcast; the watcher events still arrive later via UI-thread
            // dispatch and are idempotent against the prior notification.
            if (ResourceRegistry is not null)
            {
                var folderKeyResult = ResourceRegistry.GetResourceKey(path);
                if (folderKeyResult.IsSuccess)
                {
                    var folderRemovedMessage = new ResourceDeletedMessage(folderKeyResult.Value);
                    _messengerService.Send(folderRemovedMessage);
                }
                foreach (var key in descendantKeys)
                {
                    var descendantRemovedMessage = new ResourceDeletedMessage(key);
                    _messengerService.Send(descendantRemovedMessage);
                }
            }
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

    // Maps a pair of project-folder absolute paths to ResourceKey form so the
    // FS-layer-backed operations can address sources and destinations via key.
    // The registry returns generated keys even for destinations that don't exist
    // on disk yet, which is exactly the move/copy case.
    private Result<(ResourceKey Source, ResourceKey Destination)> ResolveOperationKeys(string sourcePath, string destPath)
    {
        var registry = ResourceRegistry;
        if (registry is null)
        {
            return Result<(ResourceKey, ResourceKey)>.Fail("Workspace is not loaded; resource registry is unavailable.");
        }

        var sourceKeyResult = registry.GetResourceKey(sourcePath);
        if (sourceKeyResult.IsFailure)
        {
            return Result<(ResourceKey, ResourceKey)>.Fail($"Failed to compute resource key for source path: '{sourcePath}'")
                .WithErrors(sourceKeyResult);
        }

        var destKeyResult = registry.GetResourceKey(destPath);
        if (destKeyResult.IsFailure)
        {
            return Result<(ResourceKey, ResourceKey)>.Fail($"Failed to compute resource key for destination path: '{destPath}'")
                .WithErrors(destKeyResult);
        }

        return Result<(ResourceKey, ResourceKey)>.Ok((sourceKeyResult.Value, destKeyResult.Value));
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
