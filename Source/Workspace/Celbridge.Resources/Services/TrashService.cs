using Celbridge.Entities;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Resources.Helpers;
using Celbridge.Workspace;

namespace Celbridge.Resources.Services;

public sealed class TrashService : ITrashService
{
    // Retry budget for cross-process sharing-violation races on file/folder
    // moves into and out of trash. Antivirus, search indexer, or sync clients
    // briefly hold a read handle on a newly-created file; matches the chokepoint's
    // own read/write/move retry budget until we have evidence trash traffic
    // wants something different.
    private const int MaxAttempts = 3;
    private const int BaseRetryDelayMs = 50;

    private readonly ILogger<TrashService> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public TrashService(
        ILogger<TrashService> logger,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
    }

    private IResourceRegistry ResourceRegistry =>
        _workspaceWrapper.WorkspaceService.ResourceService.Registry;

    private ISidecarService SidecarService =>
        _workspaceWrapper.WorkspaceService.SidecarService;

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
        ProjectConstants.CelbridgeTrashFolder);

    public async Task<Result<TrashEntry>> MoveToTrashAsync(ResourceKey resource)
    {
        var resolveResult = ResourceRegistry.ResolveResourcePath(resource);
        if (resolveResult.IsFailure)
        {
            return Result<TrashEntry>.Fail($"Failed to resolve path for resource: '{resource}'")
                .WithErrors(resolveResult);
        }
        var originalPath = resolveResult.Value;

        bool isFile = File.Exists(originalPath);
        bool isFolder = Directory.Exists(originalPath);
        if (!isFile
            && !isFolder)
        {
            return Result<TrashEntry>.Fail($"Resource does not exist: '{resource}'");
        }

        var trashId = Guid.NewGuid().ToString();
        var relativePath = Path.GetRelativePath(ResourceRegistry.ProjectFolderPath, originalPath);
        var trashBasePath = Path.Combine(TrashFolderPath, trashId);
        var trashPath = Path.Combine(trashBasePath, relativePath);

        if (isFile)
        {
            return await MoveFileToTrashAsync(resource, originalPath, trashPath, trashBasePath, trashId);
        }

        return await MoveFolderToTrashAsync(resource, originalPath, trashPath, trashBasePath, trashId);
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
            if (sidecarPathResult.IsSuccess
                && File.Exists(sidecarPathResult.Value))
            {
                sidecarOriginalPath = sidecarPathResult.Value;
                sidecarTrashPath = trashPath + SidecarHelper.Extension;
            }
        }

        string? entityDataOriginalPath = null;
        string? entityDataTrashPath = null;
        var entityService = EntityService;
        if (entityService is not null)
        {
            var candidatePath = entityService.GetEntityDataPath(resource);
            if (File.Exists(candidatePath))
            {
                entityDataOriginalPath = candidatePath;
                var entityRelative = entityService.GetEntityDataRelativePath(resource);
                entityDataTrashPath = Path.Combine(trashBasePath, entityRelative);
            }
        }

        try
        {
            ClearReadOnlyIfSet(originalPath);
            await MoveFileWithDirectoryCreationAsync(originalPath, trashPath);

            if (sidecarOriginalPath is not null
                && sidecarTrashPath is not null)
            {
                ClearReadOnlyIfSet(sidecarOriginalPath);
                await MoveFileWithDirectoryCreationAsync(sidecarOriginalPath, sidecarTrashPath);
            }

            if (entityDataOriginalPath is not null
                && entityDataTrashPath is not null)
            {
                ClearReadOnlyIfSet(entityDataOriginalPath);
                await MoveFileWithDirectoryCreationAsync(entityDataOriginalPath, entityDataTrashPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to move resource to trash: '{resource}'");
            return Result<TrashEntry>.Fail($"Failed to move resource to trash: '{resource}'")
                .WithException(ex);
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
        var files = Directory.GetFiles(originalPath);
        var directories = Directory.GetDirectories(originalPath);
        bool wasEmpty = files.Length == 0
            && directories.Length == 0;

        var descendantKeys = new List<ResourceKey>();
        var entityDataFiles = new List<TrashedEntityDataFile>();

        if (!wasEmpty)
        {
            // Walking once to gather descendant keys for messaging and entity-data
            // pairs for trash. Direct System.IO inside the trash service is
            // permitted under cm-9 Decision 7 since trash bookkeeping lives outside
            // the registry's reach.
            var entityService = EntityService;
            foreach (var filePath in Directory.GetFiles(originalPath, "*", SearchOption.AllDirectories))
            {
                var keyResult = ResourceRegistry.GetResourceKey(filePath);
                if (keyResult.IsSuccess)
                {
                    descendantKeys.Add(keyResult.Value);

                    if (entityService is not null)
                    {
                        var entityDataPath = entityService.GetEntityDataPath(keyResult.Value);
                        if (File.Exists(entityDataPath))
                        {
                            var entityRelative = entityService.GetEntityDataRelativePath(keyResult.Value);
                            var entityTrashPath = Path.Combine(trashBasePath, entityRelative);
                            entityDataFiles.Add(new TrashedEntityDataFile(entityDataPath, entityTrashPath));
                        }
                    }
                }
            }
        }

        try
        {
            ClearReadOnlyRecursive(originalPath);

            if (wasEmpty)
            {
                Directory.Delete(originalPath);
            }
            else
            {
                foreach (var entityDataFile in entityDataFiles)
                {
                    await MoveFileWithDirectoryCreationAsync(entityDataFile.OriginalPath, entityDataFile.TrashPath);
                }

                await MoveDirectoryWithParentCreationAsync(originalPath, trashPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to move folder to trash: '{resource}'");
            return Result<TrashEntry>.Fail($"Failed to move folder to trash: '{resource}'")
                .WithException(ex);
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
        try
        {
            if (entry.WasFolder)
            {
                if (entry.WasEmptyFolder)
                {
                    Directory.CreateDirectory(entry.OriginalPath);
                }
                else
                {
                    if (!Directory.Exists(entry.TrashPath))
                    {
                        return Result.Fail($"Trash folder does not exist: '{entry.TrashPath}'");
                    }

                    await MoveDirectoryWithParentCreationAsync(entry.TrashPath, entry.OriginalPath);

                    foreach (var entityDataFile in entry.EntityDataFiles)
                    {
                        if (File.Exists(entityDataFile.TrashPath))
                        {
                            await MoveFileWithDirectoryCreationAsync(entityDataFile.TrashPath, entityDataFile.OriginalPath);
                        }
                    }

                    CleanupEmptyParentDirectories(entry.TrashPath);
                }
            }
            else
            {
                if (!File.Exists(entry.TrashPath))
                {
                    return Result.Fail($"Trash file does not exist: '{entry.TrashPath}'");
                }

                await MoveFileWithDirectoryCreationAsync(entry.TrashPath, entry.OriginalPath);

                if (entry.SidecarOriginalPath is not null
                    && entry.SidecarTrashPath is not null
                    && File.Exists(entry.SidecarTrashPath))
                {
                    await MoveFileWithDirectoryCreationAsync(entry.SidecarTrashPath, entry.SidecarOriginalPath);
                }

                foreach (var entityDataFile in entry.EntityDataFiles)
                {
                    if (File.Exists(entityDataFile.TrashPath))
                    {
                        await MoveFileWithDirectoryCreationAsync(entityDataFile.TrashPath, entityDataFile.OriginalPath);
                    }
                }

                CleanupEmptyParentDirectories(entry.TrashPath);
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to restore resource from trash: '{entry.OriginalResource}'");
            return Result.Fail($"Failed to restore resource from trash: '{entry.OriginalResource}'")
                .WithException(ex);
        }
    }

    public Task<Result> PurgeAsync(TrashEntry entry)
    {
        try
        {
            if (entry.WasFolder)
            {
                if (!entry.WasEmptyFolder
                    && Directory.Exists(entry.TrashPath))
                {
                    Directory.Delete(entry.TrashPath, recursive: true);
                    CleanupEmptyParentDirectories(entry.TrashPath);
                }

                foreach (var entityDataFile in entry.EntityDataFiles)
                {
                    DeleteFileIfExists(entityDataFile.TrashPath);
                    CleanupEmptyParentDirectories(entityDataFile.TrashPath);
                }
            }
            else
            {
                DeleteFileIfExists(entry.TrashPath);

                if (entry.SidecarTrashPath is not null)
                {
                    DeleteFileIfExists(entry.SidecarTrashPath);
                }

                foreach (var entityDataFile in entry.EntityDataFiles)
                {
                    DeleteFileIfExists(entityDataFile.TrashPath);
                }

                CleanupEmptyParentDirectories(entry.TrashPath);
            }
        }
        catch (Exception ex)
        {
            // Purge runs when an undo entry is evicted or the redo stack is
            // cleared. It is best-effort cleanup; orphaned bytes inside the trash
            // folder are wiped on the next workspace load.
            _logger.LogDebug(ex, $"Best-effort trash purge failed for resource: '{entry.OriginalResource}'");
        }

        return Task.FromResult(Result.Ok());
    }

    // User intent to delete overrides the DOS read-only attribute, matching
    // Windows Explorer's "delete read-only file?" confirmation behaviour. The
    // cleared state persists through undo; a restored file lands writable and
    // the user can re-apply the attribute via the OS file properties dialog.
    private static void ClearReadOnlyIfSet(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Exists
                && info.IsReadOnly)
            {
                info.IsReadOnly = false;
            }
        }
        catch
        {
            // Best effort; surface the underlying issue from the subsequent move.
        }
    }

    // Recursive read-only clear for folder delete. Directory.Move into trash
    // (and the empty-folder Directory.Delete) fails if any contained file is
    // read-only, so traverse first.
    private static void ClearReadOnlyRecursive(string folder)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                ClearReadOnlyIfSet(file);
            }
        }
        catch
        {
            // Best effort traversal.
        }
    }

    // Move a file into the trash subtree, creating any missing parent folders
    // first. Retries briefly on transient IOException for the sharing-violation
    // race that follows AV / indexer / sync clients touching a newly-created file.
    private static async Task MoveFileWithDirectoryCreationAsync(string sourcePath, string destPath)
    {
        var destFolder = Path.GetDirectoryName(destPath)!;
        Directory.CreateDirectory(destFolder);
        await MoveWithRetryAsync(() => File.Move(sourcePath, destPath));
    }

    private static async Task MoveDirectoryWithParentCreationAsync(string sourcePath, string destPath)
    {
        var destParentFolder = Path.GetDirectoryName(destPath)!;
        Directory.CreateDirectory(destParentFolder);
        await MoveWithRetryAsync(() => Directory.Move(sourcePath, destPath));
    }

    private static async Task MoveWithRetryAsync(Action moveOperation)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                moveOperation();
                return;
            }
            catch (IOException) when (attempt < MaxAttempts)
            {
                await Task.Delay(BaseRetryDelayMs * attempt);
            }
        }
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    // Walk up from the start path removing every empty parent folder until
    // either a non-empty folder or a top-level boundary is hit. Trash bookkeeping
    // would otherwise accrue empty per-GUID folders after every purge.
    private static void CleanupEmptyParentDirectories(string startPath)
    {
        try
        {
            var folder = Path.GetDirectoryName(startPath);
            while (!string.IsNullOrEmpty(folder)
                && Directory.Exists(folder))
            {
                if (Directory.GetFiles(folder).Length == 0
                    && Directory.GetDirectories(folder).Length == 0)
                {
                    Directory.Delete(folder);
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
