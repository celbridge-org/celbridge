using Celbridge.Entities;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Resources.Helpers;
using Celbridge.Workspace;

namespace Celbridge.Resources.Services;

public sealed class TrashService : ITrashService
{
    private readonly ILogger<TrashService> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ILocalFileSystem _fileSystem;

    public TrashService(
        ILogger<TrashService> logger,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper,
        ILocalFileSystem fileSystem)
    {
        _logger = logger;
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;
        _fileSystem = fileSystem;
    }

    private IResourceRegistry ResourceRegistry =>
        _workspaceWrapper.WorkspaceService.ResourceService.Registry;

    private ISidecarService SidecarService =>
        _workspaceWrapper.WorkspaceService.ResourceService.SidecarService;

    // Entity service is workspace-scoped and may be unavailable during workspace
    // teardown. Trash operations are tolerant of a null entity service and skip
    // the entity-data cascade in that case.
    private IEntityService? EntityService =>
        _workspaceWrapper.IsWorkspacePageLoaded
            ? _workspaceWrapper.WorkspaceService.EntityService
            : null;

    private string TrashFolderPath => Path.Combine(
        ResourceRegistry.ProjectFolderPath,
        ProjectConstants.CelbridgeFolder,
        ProjectConstants.TrashFolder);

    public async Task<Result<TrashEntry>> MoveToTrashAsync(ResourceKey resource)
    {
        var resolveResult = ResourceRegistry.ResolveResourcePath(resource);
        if (resolveResult.IsFailure)
        {
            return Result<TrashEntry>.Fail($"Failed to resolve path for resource: '{resource}'")
                .WithErrors(resolveResult);
        }
        var originalPath = resolveResult.Value;

        var infoResult = await _fileSystem.GetInfoAsync(originalPath);
        if (infoResult.IsFailure)
        {
            return Result<TrashEntry>.Fail(infoResult);
        }
        var info = infoResult.Value;

        bool isFile = info.Kind == StorageItemKind.File;
        bool isFolder = info.Kind == StorageItemKind.Folder;
        if (!isFile
            && !isFolder)
        {
            return Result<TrashEntry>.Fail($"Resource does not exist: '{resource}'");
        }

        var trashId = Guid.NewGuid().ToString();
        var relativePath = Path.GetRelativePath(ResourceRegistry.ProjectFolderPath, originalPath);
        var trashBasePath = Path.Combine(TrashFolderPath, trashId);
        var trashPath = Path.Combine(trashBasePath, relativePath);

        Result<TrashEntry> moveResult;
        if (isFile)
        {
            moveResult = await MoveFileToTrashAsync(resource, originalPath, trashPath, trashBasePath, trashId);
        }
        else
        {
            moveResult = await MoveFolderToTrashAsync(resource, originalPath, trashPath, trashBasePath, trashId);
        }

        if (moveResult.IsSuccess
            && resource.Root == ResourceKey.DefaultRoot)
        {
            // Announce the soft-delete synchronously so subscribers update before
            // control returns. The watcher's own delete event still arrives later
            // via UI-thread dispatch; subscribers must treat these messages as
            // idempotent.
            var entry = moveResult.Value;

            var entryMessage = new ResourceDeletedMessage(entry.OriginalResource);
            _messengerService.Send(entryMessage);

            foreach (var descendant in entry.DescendantKeys)
            {
                var descendantMessage = new ResourceDeletedMessage(descendant);
                _messengerService.Send(descendantMessage);
            }
        }

        return moveResult;
    }

    private async Task<Result<TrashEntry>> MoveFileToTrashAsync(
        ResourceKey resource,
        string originalPath,
        string trashPath,
        string trashBasePath,
        string trashId)
    {
        string? sidecarOriginalPath = null;
        string? sidecarTrashPath = null;
        var sidecarKeyResult = SidecarService.GetSidecarKey(resource);
        if (sidecarKeyResult.IsSuccess)
        {
            var sidecarPathResult = ResourceRegistry.ResolveResourcePath(sidecarKeyResult.Value);
            if (sidecarPathResult.IsSuccess)
            {
                var sidecarInfoResult = await _fileSystem.GetInfoAsync(sidecarPathResult.Value);
                if (sidecarInfoResult.IsSuccess
                    && sidecarInfoResult.Value.Kind == StorageItemKind.File)
                {
                    sidecarOriginalPath = sidecarPathResult.Value;
                    sidecarTrashPath = trashPath + SidecarHelper.Extension;
                }
            }
        }

        string? entityDataOriginalPath = null;
        string? entityDataTrashPath = null;
        var entityService = EntityService;
        if (entityService is not null)
        {
            var candidatePath = entityService.GetEntityDataPath(resource);
            var entityInfoResult = await _fileSystem.GetInfoAsync(candidatePath);
            if (entityInfoResult.IsSuccess
                && entityInfoResult.Value.Kind == StorageItemKind.File)
            {
                entityDataOriginalPath = candidatePath;
                var entityRelative = entityService.GetEntityDataRelativePath(resource);
                entityDataTrashPath = Path.Combine(trashBasePath, entityRelative);
            }
        }

        _ = await _fileSystem.SetAttributesAsync(originalPath, FileSystemAttributes.ReadOnly, set: false);
        var primaryMoveResult = await MoveFileWithDirectoryCreationAsync(originalPath, trashPath);
        if (primaryMoveResult.IsFailure)
        {
            _logger.LogError($"Failed to move resource to trash: '{resource}'. {primaryMoveResult.DiagnosticReport}");
            return Result<TrashEntry>.Fail($"Failed to move resource to trash: '{resource}'")
                .WithErrors(primaryMoveResult);
        }

        if (sidecarOriginalPath is not null
            && sidecarTrashPath is not null)
        {
            _ = await _fileSystem.SetAttributesAsync(sidecarOriginalPath, FileSystemAttributes.ReadOnly, set: false);
            var sidecarMoveResult = await MoveFileWithDirectoryCreationAsync(sidecarOriginalPath, sidecarTrashPath);
            if (sidecarMoveResult.IsFailure)
            {
                _logger.LogError($"Failed to move sidecar to trash: '{resource}'. {sidecarMoveResult.DiagnosticReport}");
                return Result<TrashEntry>.Fail($"Failed to move sidecar to trash: '{resource}'")
                    .WithErrors(sidecarMoveResult);
            }
        }

        if (entityDataOriginalPath is not null
            && entityDataTrashPath is not null)
        {
            _ = await _fileSystem.SetAttributesAsync(entityDataOriginalPath, FileSystemAttributes.ReadOnly, set: false);
            var entityMoveResult = await MoveFileWithDirectoryCreationAsync(entityDataOriginalPath, entityDataTrashPath);
            if (entityMoveResult.IsFailure)
            {
                _logger.LogError($"Failed to move entity data to trash: '{resource}'. {entityMoveResult.DiagnosticReport}");
                return Result<TrashEntry>.Fail($"Failed to move entity data to trash: '{resource}'")
                    .WithErrors(entityMoveResult);
            }
        }

        var entityDataFiles = entityDataOriginalPath is not null && entityDataTrashPath is not null
            ? new List<TrashedEntityDataFile> { new(entityDataOriginalPath, entityDataTrashPath) }
            : (IReadOnlyList<TrashedEntityDataFile>)Array.Empty<TrashedEntityDataFile>();

        var entry = new TrashEntry(
            OriginalResource: resource,
            TrashId: trashId,
            WasFolder: false,
            WasEmptyFolder: false,
            OriginalPath: originalPath,
            TrashPath: trashPath,
            SidecarOriginalPath: sidecarOriginalPath,
            SidecarTrashPath: sidecarTrashPath,
            EntityDataFiles: entityDataFiles,
            DescendantKeys: Array.Empty<ResourceKey>());

        return entry;
    }

    private async Task<Result<TrashEntry>> MoveFolderToTrashAsync(
        ResourceKey resource,
        string originalPath,
        string trashPath,
        string trashBasePath,
        string trashId)
    {
        var topLevelResult = await _fileSystem.EnumerateAsync(originalPath, "*", recursive: false);
        if (topLevelResult.IsFailure)
        {
            return Result<TrashEntry>.Fail($"Failed to enumerate folder for trash: '{resource}'")
                .WithErrors(topLevelResult);
        }
        bool wasEmpty = topLevelResult.Value.Count == 0;

        var descendantKeys = new List<ResourceKey>();
        var entityDataFiles = new List<TrashedEntityDataFile>();

        if (!wasEmpty)
        {
            // Walk once to gather descendant keys for messaging and entity-data
            // pairs for trash. Both nested files and nested sub-folders are
            // first-class resources, so every entry contributes its key to the
            // fan-out; entity data is keyed per resource and trashed for files.
            // The trash folder lives outside the registry's reach, so direct
            // enumeration here is intentional; gateway dispatch would fail
            // because the trash paths do not resolve to ResourceKeys.
            var entityService = EntityService;
            var descendantsResult = await _fileSystem.EnumerateAsync(originalPath, "*", recursive: true);
            if (descendantsResult.IsFailure)
            {
                return Result<TrashEntry>.Fail($"Failed to enumerate folder for trash: '{resource}'")
                    .WithErrors(descendantsResult);
            }
            foreach (var descendant in descendantsResult.Value)
            {
                var keyResult = ResourceRegistry.GetResourceKey(descendant.FullPath);
                if (keyResult.IsFailure)
                {
                    continue;
                }
                descendantKeys.Add(keyResult.Value);

                if (descendant.IsFolder
                    || entityService is null)
                {
                    continue;
                }

                var entityDataPath = entityService.GetEntityDataPath(keyResult.Value);
                var entityInfoResult = await _fileSystem.GetInfoAsync(entityDataPath);
                if (entityInfoResult.IsSuccess
                    && entityInfoResult.Value.Kind == StorageItemKind.File)
                {
                    var entityRelative = entityService.GetEntityDataRelativePath(keyResult.Value);
                    var entityTrashPath = Path.Combine(trashBasePath, entityRelative);
                    entityDataFiles.Add(new TrashedEntityDataFile(entityDataPath, entityTrashPath));
                }
            }
        }

        await ClearReadOnlyAttributesRecursiveAsync(originalPath);

        if (wasEmpty)
        {
            var deleteResult = await _fileSystem.DeleteFolderAsync(originalPath, recursive: false);
            if (deleteResult.IsFailure)
            {
                _logger.LogError($"Failed to remove empty folder for trash: '{resource}'. {deleteResult.DiagnosticReport}");
                return Result<TrashEntry>.Fail($"Failed to move folder to trash: '{resource}'")
                    .WithErrors(deleteResult);
            }
        }
        else
        {
            foreach (var entityDataFile in entityDataFiles)
            {
                var entityMoveResult = await MoveFileWithDirectoryCreationAsync(entityDataFile.OriginalPath, entityDataFile.TrashPath);
                if (entityMoveResult.IsFailure)
                {
                    _logger.LogError($"Failed to move entity data to trash: '{resource}'. {entityMoveResult.DiagnosticReport}");
                    return Result<TrashEntry>.Fail($"Failed to move folder to trash: '{resource}'")
                        .WithErrors(entityMoveResult);
                }
            }

            var folderMoveResult = await MoveDirectoryWithParentCreationAsync(originalPath, trashPath);
            if (folderMoveResult.IsFailure)
            {
                _logger.LogError($"Failed to move folder to trash: '{resource}'. {folderMoveResult.DiagnosticReport}");
                return Result<TrashEntry>.Fail($"Failed to move folder to trash: '{resource}'")
                    .WithErrors(folderMoveResult);
            }
        }

        var entry = new TrashEntry(
            OriginalResource: resource,
            TrashId: trashId,
            WasFolder: true,
            WasEmptyFolder: wasEmpty,
            OriginalPath: originalPath,
            TrashPath: wasEmpty ? string.Empty : trashPath,
            SidecarOriginalPath: null,
            SidecarTrashPath: null,
            EntityDataFiles: entityDataFiles,
            DescendantKeys: descendantKeys);

        return entry;
    }

    public async Task<Result> RestoreFromTrashAsync(TrashEntry entry)
    {
        if (entry.WasFolder)
        {
            if (entry.WasEmptyFolder)
            {
                var createResult = await _fileSystem.CreateFolderAsync(entry.OriginalPath);
                if (createResult.IsFailure)
                {
                    return Result.Fail($"Failed to restore empty folder: '{entry.OriginalResource}'")
                        .WithErrors(createResult);
                }
            }
            else
            {
                var trashInfoResult = await _fileSystem.GetInfoAsync(entry.TrashPath);
                if (trashInfoResult.IsFailure
                    || trashInfoResult.Value.Kind != StorageItemKind.Folder)
                {
                    return Result.Fail($"Trash folder does not exist: '{entry.TrashPath}'");
                }

                var moveResult = await MoveDirectoryWithParentCreationAsync(entry.TrashPath, entry.OriginalPath);
                if (moveResult.IsFailure)
                {
                    return Result.Fail($"Failed to restore folder from trash: '{entry.OriginalResource}'")
                        .WithErrors(moveResult);
                }

                foreach (var entityDataFile in entry.EntityDataFiles)
                {
                    var trashEntityInfoResult = await _fileSystem.GetInfoAsync(entityDataFile.TrashPath);
                    if (trashEntityInfoResult.IsSuccess
                        && trashEntityInfoResult.Value.Kind == StorageItemKind.File)
                    {
                        var restoreResult = await MoveFileWithDirectoryCreationAsync(entityDataFile.TrashPath, entityDataFile.OriginalPath);
                        if (restoreResult.IsFailure)
                        {
                            return Result.Fail($"Failed to restore entity data from trash: '{entry.OriginalResource}'")
                                .WithErrors(restoreResult);
                        }
                    }
                }

                await CleanupEmptyParentDirectoriesAsync(entry.TrashPath);
            }
        }
        else
        {
            var trashFileInfoResult = await _fileSystem.GetInfoAsync(entry.TrashPath);
            if (trashFileInfoResult.IsFailure
                || trashFileInfoResult.Value.Kind != StorageItemKind.File)
            {
                return Result.Fail($"Trash file does not exist: '{entry.TrashPath}'");
            }

            var moveResult = await MoveFileWithDirectoryCreationAsync(entry.TrashPath, entry.OriginalPath);
            if (moveResult.IsFailure)
            {
                return Result.Fail($"Failed to restore file from trash: '{entry.OriginalResource}'")
                    .WithErrors(moveResult);
            }

            if (entry.SidecarOriginalPath is not null
                && entry.SidecarTrashPath is not null)
            {
                var sidecarInfoResult = await _fileSystem.GetInfoAsync(entry.SidecarTrashPath);
                if (sidecarInfoResult.IsSuccess
                    && sidecarInfoResult.Value.Kind == StorageItemKind.File)
                {
                    var sidecarRestoreResult = await MoveFileWithDirectoryCreationAsync(entry.SidecarTrashPath, entry.SidecarOriginalPath);
                    if (sidecarRestoreResult.IsFailure)
                    {
                        return Result.Fail($"Failed to restore sidecar from trash: '{entry.OriginalResource}'")
                            .WithErrors(sidecarRestoreResult);
                    }
                }
            }

            foreach (var entityDataFile in entry.EntityDataFiles)
            {
                var trashEntityInfoResult = await _fileSystem.GetInfoAsync(entityDataFile.TrashPath);
                if (trashEntityInfoResult.IsSuccess
                    && trashEntityInfoResult.Value.Kind == StorageItemKind.File)
                {
                    var restoreResult = await MoveFileWithDirectoryCreationAsync(entityDataFile.TrashPath, entityDataFile.OriginalPath);
                    if (restoreResult.IsFailure)
                    {
                        return Result.Fail($"Failed to restore entity data from trash: '{entry.OriginalResource}'")
                            .WithErrors(restoreResult);
                    }
                }
            }

            await CleanupEmptyParentDirectoriesAsync(entry.TrashPath);
        }

        return Result.Ok();
    }

    public async Task<Result> PurgeAsync(TrashEntry entry)
    {
        // Purge runs when an undo entry is evicted or the redo stack is
        // cleared. It is best-effort cleanup; orphaned bytes inside the trash
        // folder are wiped on the next workspace load. Logged at Warning on
        // failure because a failure here can indicate a real problem (locked
        // file, permissions, disk error) that the user benefits from seeing.
        try
        {
            if (entry.WasFolder)
            {
                if (!entry.WasEmptyFolder)
                {
                    var trashInfoResult = await _fileSystem.GetInfoAsync(entry.TrashPath);
                    if (trashInfoResult.IsSuccess
                        && trashInfoResult.Value.Kind == StorageItemKind.Folder)
                    {
                        var deleteResult = await _fileSystem.DeleteFolderAsync(entry.TrashPath, recursive: true);
                        if (deleteResult.IsFailure)
                        {
                            _logger.LogWarning($"Best-effort trash purge failed for resource: '{entry.OriginalResource}'. {deleteResult.DiagnosticReport}");
                        }
                        await CleanupEmptyParentDirectoriesAsync(entry.TrashPath);
                    }
                }

                foreach (var entityDataFile in entry.EntityDataFiles)
                {
                    await DeleteFileIfExistsAsync(entityDataFile.TrashPath);
                    await CleanupEmptyParentDirectoriesAsync(entityDataFile.TrashPath);
                }
            }
            else
            {
                await DeleteFileIfExistsAsync(entry.TrashPath);

                if (entry.SidecarTrashPath is not null)
                {
                    await DeleteFileIfExistsAsync(entry.SidecarTrashPath);
                }

                foreach (var entityDataFile in entry.EntityDataFiles)
                {
                    await DeleteFileIfExistsAsync(entityDataFile.TrashPath);
                }

                await CleanupEmptyParentDirectoriesAsync(entry.TrashPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Best-effort trash purge failed for resource: '{entry.OriginalResource}'");
        }

        return Result.Ok();
    }

    // Move a file into the trash subtree, creating any missing parent folders
    // first. The gateway's retry policy handles transient sharing violations
    // from antivirus, indexers, and sync clients.
    private async Task<Result> MoveFileWithDirectoryCreationAsync(string sourcePath, string destPath)
    {
        var destFolder = Path.GetDirectoryName(destPath)!;
        var createResult = await _fileSystem.CreateFolderAsync(destFolder);
        if (createResult.IsFailure)
        {
            return createResult;
        }
        return await _fileSystem.MoveFileAsync(sourcePath, destPath);
    }

    private async Task<Result> MoveDirectoryWithParentCreationAsync(string sourcePath, string destPath)
    {
        var destParentFolder = Path.GetDirectoryName(destPath)!;
        var createResult = await _fileSystem.CreateFolderAsync(destParentFolder);
        if (createResult.IsFailure)
        {
            return createResult;
        }
        return await _fileSystem.MoveFolderAsync(sourcePath, destPath);
    }

    private async Task DeleteFileIfExistsAsync(string path)
    {
        var infoResult = await _fileSystem.GetInfoAsync(path);
        if (infoResult.IsSuccess
            && infoResult.Value.Kind == StorageItemKind.File)
        {
            var deleteResult = await _fileSystem.DeleteFileAsync(path);
            _ = deleteResult;
        }
    }

    private async Task ClearReadOnlyAttributesRecursiveAsync(string folder)
    {
        var enumerateResult = await _fileSystem.EnumerateAsync(folder, "*", recursive: true);
        if (enumerateResult.IsFailure)
        {
            return;
        }
        foreach (var entry in enumerateResult.Value)
        {
            if (entry.IsFolder)
            {
                continue;
            }
            _ = await _fileSystem.SetAttributesAsync(entry.FullPath, FileSystemAttributes.ReadOnly, set: false);
        }
    }

    // Walk up from the start path removing every empty parent folder until
    // either a non-empty folder or a top-level boundary is hit. Trash bookkeeping
    // would otherwise accrue empty per-GUID folders after every purge.
    private async Task CleanupEmptyParentDirectoriesAsync(string startPath)
    {
        try
        {
            var folder = Path.GetDirectoryName(startPath);
            while (!string.IsNullOrEmpty(folder))
            {
                var folderInfoResult = await _fileSystem.GetInfoAsync(folder);
                if (folderInfoResult.IsFailure
                    || folderInfoResult.Value.Kind != StorageItemKind.Folder)
                {
                    break;
                }

                var enumerateResult = await _fileSystem.EnumerateAsync(folder, "*", recursive: false);
                if (enumerateResult.IsFailure)
                {
                    break;
                }

                if (enumerateResult.Value.Count == 0)
                {
                    var deleteResult = await _fileSystem.DeleteFolderAsync(folder, recursive: false);
                    if (deleteResult.IsFailure)
                    {
                        break;
                    }
                    folder = Path.GetDirectoryName(folder);
                }
                else
                {
                    break;
                }
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }
}
