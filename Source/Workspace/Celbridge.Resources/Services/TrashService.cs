using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Resources.Helpers;
using Celbridge.Workspace;

namespace Celbridge.Resources.Services;

public sealed class TrashService : ITrashService
{
    private readonly ILogger<TrashService> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectService _projectService;
    private readonly ILocalFileSystem _fileSystem;

    public TrashService(
        ILogger<TrashService> logger,
        IWorkspaceWrapper workspaceWrapper,
        IProjectService projectService,
        ILocalFileSystem fileSystem)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
        _projectService = projectService;
        _fileSystem = fileSystem;
    }

    private IRootHandlerRegistry RootHandlers =>
        _workspaceWrapper.WorkspaceService.ResourceService.RootHandlers;

    private ISidecarService SidecarService =>
        _workspaceWrapper.WorkspaceService.ResourceService.Sidecars;

    private string TrashFolderPath => Path.Combine(
        _projectService.CurrentProject!.ProjectDataFolderPath,
        ProjectConstants.TrashFolder);

    public async Task<Result<TrashEntry>> MoveToTrashAsync(ResourceKey resource)
    {
        // The trash stores each item at its path relative to the project folder.
        if (resource.Root != ResourceKey.DefaultRoot)
        {
            return Result<TrashEntry>.Fail($"Only resources under the project root go to the trash: '{resource}'");
        }

        var resolveResult = RootHandlers.ResolveResourcePath(resource);
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

        if (info.IsLink)
        {
            return RefuseLink(originalPath);
        }

        var trashId = Guid.NewGuid().ToString();
        var relativePath = Path.GetRelativePath(_projectService.CurrentProject!.ProjectFolderPath, originalPath);
        var trashBasePath = Path.Combine(TrashFolderPath, trashId);
        var trashPath = Path.Combine(trashBasePath, relativePath);

        using var changes = CreateChangeRecorder();

        if (isFile)
        {
            return await MoveFileToTrashAsync(resource, originalPath, trashPath, trashBasePath, trashId, changes);
        }

        return await MoveFolderToTrashAsync(resource, originalPath, trashPath, trashBasePath, trashId, changes);
    }

    private async Task<Result<TrashEntry>> MoveFileToTrashAsync(
        ResourceKey resource,
        string originalPath,
        string trashPath,
        string trashBasePath,
        string trashId,
        ChangeRecorder changes)
    {
        string? sidecarOriginalPath = null;
        string? sidecarTrashPath = null;
        var sidecarKeyResult = SidecarService.GetSidecarKey(resource);
        if (sidecarKeyResult.IsSuccess)
        {
            var sidecarPathResult = RootHandlers.ResolveResourcePath(sidecarKeyResult.Value);
            if (sidecarPathResult.IsSuccess)
            {
                var sidecarInfoResult = await _fileSystem.GetInfoAsync(sidecarPathResult.Value);
                if (sidecarInfoResult.IsSuccess
                    && sidecarInfoResult.Value.Kind == StorageItemKind.File)
                {
                    sidecarOriginalPath = sidecarPathResult.Value;
                    sidecarTrashPath = trashPath + SidecarFile.Extension;
                }
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
        changes.RecordRemoved(resource, originalPath);

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
            changes.RecordRemoved(sidecarKeyResult.Value, sidecarOriginalPath);
        }

        var entry = new TrashEntry(
            OriginalResource: resource,
            TrashId: trashId,
            WasFolder: false,
            WasEmptyFolder: false,
            OriginalPath: originalPath,
            TrashPath: trashPath,
            SidecarOriginalPath: sidecarOriginalPath,
            SidecarTrashPath: sidecarTrashPath);

        return entry;
    }

    private async Task<Result<TrashEntry>> MoveFolderToTrashAsync(
        ResourceKey resource,
        string originalPath,
        string trashPath,
        string trashBasePath,
        string trashId,
        ChangeRecorder changes)
    {
        var topLevelResult = await _fileSystem.EnumerateAsync(originalPath, "*", recursive: false);
        if (topLevelResult.IsFailure)
        {
            return Result<TrashEntry>.Fail($"Failed to enumerate folder for trash: '{resource}'")
                .WithErrors(topLevelResult);
        }
        bool wasEmpty = topLevelResult.Value.Count == 0;

        // The trash refuses a folder that contains a link.
        if (!wasEmpty)
        {
            var descendantsResult = await _fileSystem.EnumerateAsync(originalPath, "*", recursive: true);
            if (descendantsResult.IsFailure)
            {
                return Result<TrashEntry>.Fail($"Failed to enumerate folder for trash: '{resource}'")
                    .WithErrors(descendantsResult);
            }
            foreach (var descendant in descendantsResult.Value)
            {
                if (descendant.IsLink)
                {
                    return RefuseLink(descendant.FullPath);
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
            var folderMoveResult = await MoveDirectoryWithParentCreationAsync(originalPath, trashPath);
            if (folderMoveResult.IsFailure)
            {
                _logger.LogError($"Failed to move folder to trash: '{resource}'. {folderMoveResult.DiagnosticReport}");
                return Result<TrashEntry>.Fail($"Failed to move folder to trash: '{resource}'")
                    .WithErrors(folderMoveResult);
            }
        }
        changes.RecordRemoved(resource, originalPath);

        var entry = new TrashEntry(
            OriginalResource: resource,
            TrashId: trashId,
            WasFolder: true,
            WasEmptyFolder: wasEmpty,
            OriginalPath: originalPath,
            TrashPath: wasEmpty ? string.Empty : trashPath,
            SidecarOriginalPath: null,
            SidecarTrashPath: null);

        return entry;
    }

    public async Task<Result> RestoreFromTrashAsync(TrashEntry entry)
    {
        using var changes = CreateChangeRecorder();

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
                changes.RecordCreated(entry.OriginalResource, entry.OriginalPath);
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
                changes.RecordCreated(entry.OriginalResource, entry.OriginalPath);

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
            changes.RecordCreated(entry.OriginalResource, entry.OriginalPath);

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

                    var sidecarKeyResult = SidecarService.GetSidecarKey(entry.OriginalResource);
                    if (sidecarKeyResult.IsSuccess)
                    {
                        changes.RecordCreated(sidecarKeyResult.Value, entry.SidecarOriginalPath);
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
            }
            else
            {
                await DeleteFileIfExistsAsync(entry.TrashPath);

                if (entry.SidecarTrashPath is not null)
                {
                    await DeleteFileIfExistsAsync(entry.SidecarTrashPath);
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

    private ChangeRecorder CreateChangeRecorder()
    {
        var resourceService = _workspaceWrapper.WorkspaceService.ResourceService;
        return new ChangeRecorder(resourceService);
    }

    // If the trash is on another volume, moving an item there copies it, and a copy replaces a link with its
    // target. So the trash refuses links and leaves the item in place.
    private static Result<TrashEntry> RefuseLink(string linkPath)
    {
        var error = new ResourceContainsLinkError(linkPath);

        return Result<TrashEntry>.Fail(error.Message).WithException(error);
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
