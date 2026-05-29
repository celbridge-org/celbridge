using System.Text;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Utilities;
using Celbridge.Workspace;

namespace Celbridge.Resources.Services;

public sealed class FileStorage : IFileStorage
{
    // Buffer size used when opening file streams. Matches the default System.IO
    // FileStream buffer size when none is supplied.
    private const int StreamBufferSize = 4096;

    private readonly ILogger<FileStorage> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly SidecarCascade _sidecarCascade;
    private readonly ReferenceRewriter _referenceRewriter;

    // The resource registry is workspace-scoped and transient: a constructor-
    // injected instance is a different object from the one held by ResourceService,
    // and only the ResourceService instance has ProjectFolderPath set. The
    // file-system layer resolves the live registry through the workspace wrapper
    // at call time.
    public FileStorage(
        ILogger<FileStorage> logger,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;
        _sidecarCascade = new SidecarCascade(logger, workspaceWrapper);
        _referenceRewriter = new ReferenceRewriter(logger, workspaceWrapper, this);
    }

    public async Task<Result<byte[]>> ReadAllBytesAsync(ResourceKey resource)
    {
        var resolveResult = ResolvePath(resource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{resource}'")
                .WithErrors(resolveResult);
        }
        var resourcePath = resolveResult.Value;

        return await FileStorageInternals.RunWithRetryAsync<byte[]>(
            _logger,
            operationLabel: "Read",
            resourceLabel: resource,
            resourcePath: resourcePath,
            operation: () => File.ReadAllBytesAsync(resourcePath),
            shouldRetry: IsTransientReadIOException);
    }

    public async Task<Result<string>> ReadAllTextAsync(ResourceKey resource)
    {
        var resolveResult = ResolvePath(resource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{resource}'")
                .WithErrors(resolveResult);
        }
        var resourcePath = resolveResult.Value;

        return await FileStorageInternals.RunWithRetryAsync<string>(
            _logger,
            operationLabel: "Read",
            resourceLabel: resource,
            resourcePath: resourcePath,
            operation: () => File.ReadAllTextAsync(resourcePath),
            shouldRetry: IsTransientReadIOException);
    }

    public async Task<Result<Stream>> OpenReadAsync(ResourceKey resource)
    {
        var resolveResult = ResolvePath(resource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{resource}'")
                .WithErrors(resolveResult);
        }
        var resourcePath = resolveResult.Value;

        return await FileStorageInternals.RunWithRetryAsync<Stream>(
            _logger,
            operationLabel: "Read",
            resourceLabel: resource,
            resourcePath: resourcePath,
            operation: () => Task.FromResult<Stream>(new FileStream(
                resourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                StreamBufferSize,
                useAsync: true)),
            shouldRetry: IsTransientReadIOException);
    }

    // Reads short-circuit FileNotFoundException and DirectoryNotFoundException
    // so a genuinely missing file fails immediately rather than burning the
    // retry budget on a hopeless case.
    private static bool IsTransientReadIOException(IOException ex)
    {
        return ex is not FileNotFoundException
            and not DirectoryNotFoundException;
    }

    public Task<Result> WriteAllBytesAsync(ResourceKey resource, byte[] bytes)
    {
        return WriteWithRetryAsync(resource, bytes);
    }

    public Task<Result> WriteAllTextAsync(ResourceKey resource, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return WriteWithRetryAsync(resource, bytes);
    }

    public async Task<Result<Stream>> OpenWriteAsync(ResourceKey resource)
    {
        await Task.CompletedTask;

        var resolveResult = ResolvePath(resource);
        if (resolveResult.IsFailure)
        {
            var failure = Result.Fail($"Failed to resolve path for resource: '{resource}'")
                .WithErrors(resolveResult);
            return failure;
        }
        var resourcePath = resolveResult.Value;

        var ensureParentResult = EnsureParentFolderExists(resourcePath, resource);
        if (ensureParentResult.IsFailure)
        {
            return Result.Fail(ensureParentResult);
        }

        try
        {
            // FileShare.None (not FileShare.Read) is deliberate: while a write
            // stream is open no other process can read partial bytes. The
            // trade-off is that another reader hitting the file mid-write sees
            // a sharing-violation IOException, not stale-or-partial content.
            // Unlike the buffered-bytes write paths, this stream writes directly
            // to the destination: a crash or unhandled exception before the
            // stream is fully written and disposed truncates the file in place.
            var stream = new FileStream(
                resourcePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                StreamBufferSize,
                useAsync: true);
            return stream;
        }
        catch (Exception ex)
        {
            var failure = Result.Fail($"Failed to open write stream for resource: '{resource}'")
                .WithException(ex);
            return failure;
        }
    }

    public async Task<Result<MoveResult>> MoveAsync(ResourceKey source, ResourceKey dest)
    {
        if (source.Root != dest.Root)
        {
            return Result.Fail($"Cross-root move not supported: '{source}' to '{dest}'");
        }

        var registry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolveSourceResult = registry.ResolveResourcePath(source);
        if (resolveSourceResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for source resource: '{source}'")
                .WithErrors(resolveSourceResult);
        }
        var sourcePath = resolveSourceResult.Value;

        var resolveDestResult = registry.ResolveResourcePath(dest);
        if (resolveDestResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for destination resource: '{dest}'")
                .WithErrors(resolveDestResult);
        }
        var destPath = resolveDestResult.Value;

        bool sourceIsFile = File.Exists(sourcePath);
        bool sourceIsFolder = Directory.Exists(sourcePath);
        if (!sourceIsFile
            && !sourceIsFolder)
        {
            return Result.Fail($"Source resource does not exist: '{source}'");
        }

        var rootHandlerRegistry = _workspaceWrapper.WorkspaceService.ResourceService.RootHandlerRegistry;
        if (!IsRootWritable(rootHandlerRegistry, dest))
        {
            return Result.Fail($"Root '{dest.Root}' is read-only.");
        }

        bool isSameLocation = string.Equals(sourcePath, destPath, StringComparison.OrdinalIgnoreCase);
        if (!isSameLocation
            && (File.Exists(destPath) || Directory.Exists(destPath)))
        {
            return Result.Fail($"Destination already exists: '{dest}'");
        }

        var updatedReferencers = new List<ResourceKey>();
        var skippedReferencers = new List<SkippedReferencer>();

        if (source.Root == ResourceKey.DefaultRoot)
        {
            var rewriteResult = await _referenceRewriter.RewriteForMoveAsync(source, dest, sourceIsFolder, updatedReferencers, skippedReferencers);
            if (rewriteResult.IsFailure)
            {
                return Result.Fail(rewriteResult);
            }
        }

        // Capture descendant keys (folders only) before the disk move so the
        // post-move eager-notify can drop their stale source-side index
        // entries. After Directory.Move the source path is gone and the
        // enumeration is no longer possible.
        var sourceDescendantKeys = sourceIsFolder
            ? EnumerateDescendantKeys(rootHandlerRegistry, sourcePath)
            : Array.Empty<ResourceKey>();

        try
        {
            var destParent = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destParent)
                && !Directory.Exists(destParent))
            {
                Directory.CreateDirectory(destParent);
            }

            if (sourceIsFile)
            {
                // Clear read-only so the move itself is not blocked by an
                // attribute the user has explicitly chosen to override by
                // invoking a move on the file.
                FileStorageInternals.ClearReadOnlyIfSet(sourcePath);
                await FileStorageInternals.RetryTransientIOAsync(_logger, "Move", sourcePath, () => File.Move(sourcePath, destPath));
            }
            else
            {
                await FileStorageInternals.RetryTransientIOAsync(_logger, "Move", sourcePath, () => Directory.Move(sourcePath, destPath));
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Fail($"Failed to move resource '{source}' to '{dest}': access denied (permissions or file in use).")
                .WithException(ex);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to move resource: '{source}' to '{dest}'")
                .WithException(ex);
        }

        var sidecarOutcome = await _sidecarCascade.TryMoveAsync(source, dest);

        if (source.Root == ResourceKey.DefaultRoot)
        {
            // Announce the source removal and the new key identity synchronously
            // so subscribers update before control returns. The watcher's own
            // events still arrive later via UI-thread dispatch; subscribers must
            // treat these messages as idempotent.
            var sourceRemovedMessage = new ResourceDeletedMessage(source);
            _messengerService.Send(sourceRemovedMessage);

            var keyChangedMessage = new ResourceKeyChangedMessage(source, dest);
            _messengerService.Send(keyChangedMessage);

            foreach (var descendantSource in sourceDescendantKeys)
            {
                var descendantRemovedMessage = new ResourceDeletedMessage(descendantSource);
                _messengerService.Send(descendantRemovedMessage);

                if (TryMapDescendantKey(source, dest, descendantSource, out var descendantDestination))
                {
                    var descendantKeyChangedMessage = new ResourceKeyChangedMessage(descendantSource, descendantDestination);
                    _messengerService.Send(descendantKeyChangedMessage);
                }
            }
        }

        await Task.CompletedTask;

        var moveResult = new MoveResult(updatedReferencers, skippedReferencers, sidecarOutcome);
        return moveResult;
    }

    // Maps a descendant of the source folder to the equivalent key under the
    // destination folder.
    private static bool TryMapDescendantKey(
        ResourceKey sourceFolder,
        ResourceKey destFolder,
        ResourceKey descendantSource,
        out ResourceKey descendantDestination)
    {
        var sourcePath = sourceFolder.Path;
        var descendantPath = descendantSource.Path;
        if (!descendantPath.StartsWith(sourcePath + "/", StringComparison.Ordinal))
        {
            descendantDestination = ResourceKey.Empty;
            return false;
        }

        var relativeSuffix = descendantPath.Substring(sourcePath.Length);
        var destPath = destFolder.Path + relativeSuffix;
        var rootName = destFolder.Root;
        var fullKey = rootName == ResourceKey.DefaultRoot
            ? destPath
            : $"{rootName}:{destPath}";
        return ResourceKey.TryCreate(fullKey, out descendantDestination);
    }

    public async Task<Result<CopyResult>> CopyAsync(ResourceKey source, ResourceKey dest)
    {
        var registry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolveSourceResult = registry.ResolveResourcePath(source);
        if (resolveSourceResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for source resource: '{source}'")
                .WithErrors(resolveSourceResult);
        }
        var sourcePath = resolveSourceResult.Value;

        var resolveDestResult = registry.ResolveResourcePath(dest);
        if (resolveDestResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for destination resource: '{dest}'")
                .WithErrors(resolveDestResult);
        }
        var destPath = resolveDestResult.Value;

        bool sourceIsFile = File.Exists(sourcePath);
        bool sourceIsFolder = Directory.Exists(sourcePath);
        if (!sourceIsFile
            && !sourceIsFolder)
        {
            return Result.Fail($"Source resource does not exist: '{source}'");
        }

        var rootHandlerRegistry = _workspaceWrapper.WorkspaceService.ResourceService.RootHandlerRegistry;
        if (!IsRootWritable(rootHandlerRegistry, dest))
        {
            return Result.Fail($"Root '{dest.Root}' is read-only.");
        }

        if (File.Exists(destPath)
            || Directory.Exists(destPath))
        {
            return Result.Fail($"Destination already exists: '{dest}'");
        }

        try
        {
            var destParent = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destParent)
                && !Directory.Exists(destParent))
            {
                Directory.CreateDirectory(destParent);
            }

            if (sourceIsFile)
            {
                await FileStorageInternals.RetryTransientIOAsync(_logger, "Copy", sourcePath, () => File.Copy(sourcePath, destPath));
            }
            else
            {
                await FileStorageInternals.RetryTransientIOAsync(_logger, "Copy", sourcePath, () => CopyFolderRecursive(sourcePath, destPath));
            }
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to copy resource: '{source}' to '{dest}'")
                .WithException(ex);
        }

        var sidecarOutcome = _sidecarCascade.TryCopy(source, dest);

        var copyResult = new CopyResult(sidecarOutcome);
        return copyResult;
    }

    public async Task<Result<DeleteResult>> DeleteAsync(ResourceKey source)
    {
        var registry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolveResult = registry.ResolveResourcePath(source);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{source}'")
                .WithErrors(resolveResult);
        }
        var sourcePath = resolveResult.Value;

        bool sourceIsFile = File.Exists(sourcePath);
        bool sourceIsFolder = Directory.Exists(sourcePath);
        if (!sourceIsFile
            && !sourceIsFolder)
        {
            return Result.Fail($"Resource does not exist: '{source}'");
        }

        var rootHandlerRegistry = _workspaceWrapper.WorkspaceService.ResourceService.RootHandlerRegistry;
        if (!IsRootWritable(rootHandlerRegistry, source))
        {
            return Result.Fail($"Root '{source.Root}' is read-only.");
        }

        var sidecarOutcome = _sidecarCascade.TryDelete(source);

        // Capture descendant keys (folders only) before the disk delete so the
        // post-delete eager-notify can drop their stale index entries too.
        var descendantKeys = sourceIsFolder
            ? EnumerateDescendantKeys(rootHandlerRegistry, sourcePath)
            : Array.Empty<ResourceKey>();

        try
        {
            if (sourceIsFile)
            {
                // Clear read-only so File.Delete doesn't trip on the attribute.
                // Matches OS Explorer's "delete read-only file?" behaviour
                // (proceed when the user explicitly invokes delete).
                FileStorageInternals.ClearReadOnlyIfSet(sourcePath);
                await FileStorageInternals.RetryTransientIOAsync(_logger, "Delete", sourcePath, () => File.Delete(sourcePath));
            }
            else
            {
                // Recursive delete fails on any contained read-only file, so
                // strip the attribute throughout the subtree first.
                FileStorageInternals.ClearReadOnlyRecursive(sourcePath);
                await FileStorageInternals.RetryTransientIOAsync(_logger, "Delete", sourcePath, () => Directory.Delete(sourcePath, recursive: true));
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Fail($"Failed to delete resource '{source}': access denied (permissions or file in use).")
                .WithException(ex);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to delete resource: '{source}'")
                .WithException(ex);
        }

        if (source.Root == ResourceKey.DefaultRoot)
        {
            // Announce the removal synchronously so subscribers update before
            // control returns. The watcher's own delete event still arrives
            // later via UI-thread dispatch; subscribers must treat these
            // messages as idempotent.
            var sourceRemovedMessage = new ResourceDeletedMessage(source);
            _messengerService.Send(sourceRemovedMessage);
            foreach (var key in descendantKeys)
            {
                var descendantRemovedMessage = new ResourceDeletedMessage(key);
                _messengerService.Send(descendantRemovedMessage);
            }
        }

        var deleteResult = new DeleteResult(sidecarOutcome);
        return deleteResult;
    }

    // Returns the resource keys of every file inside a folder that exists on
    // disk. Used to capture descendant keys before a recursive delete or move
    // so eager-notify can drop their stale entries from the reference index.
    private static IReadOnlyList<ResourceKey> EnumerateDescendantKeys(IRootHandlerRegistry rootHandlerRegistry, string folderPath)
    {
        var keys = new List<ResourceKey>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories))
            {
                var keyResult = rootHandlerRegistry.GetResourceKey(file);
                if (keyResult.IsSuccess)
                {
                    keys.Add(keyResult.Value);
                }
            }
        }
        catch
        {
            // Best effort. A failure here just means descendant keys won't be
            // eager-notified; the watcher events still arrive eventually and
            // clean up the index.
        }
        return keys;
    }

    public async Task<Result> CreateFolderAsync(ResourceKey folder)
    {
        await Task.CompletedTask;

        var resolveResult = ResolvePath(folder);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{folder}'")
                .WithErrors(resolveResult);
        }
        var folderPath = resolveResult.Value;

        var rootHandlerRegistry = _workspaceWrapper.WorkspaceService.ResourceService.RootHandlerRegistry;
        if (!IsRootWritable(rootHandlerRegistry, folder))
        {
            return Result.Fail($"Root '{folder.Root}' is read-only.");
        }

        if (File.Exists(folderPath))
        {
            return Result.Fail($"Cannot create folder; a file already exists at: '{folder}'");
        }

        try
        {
            // Directory.CreateDirectory is idempotent: existing folders return
            // the DirectoryInfo without error, and missing intermediate parents
            // are created in the same call.
            Directory.CreateDirectory(folderPath);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to create folder: '{folder}'")
                .WithException(ex);
        }
    }

    public async Task<Result<StorageItemInfo>> GetInfoAsync(ResourceKey resource)
    {
        await Task.CompletedTask;

        var resolveResult = ResolvePath(resource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{resource}'")
                .WithErrors(resolveResult);
        }
        var resourcePath = resolveResult.Value;

        try
        {
            // File.Exists, FileInfo.Length, and FileInfo.LastWriteTimeUtc share
            // the same underlying stat() call; populating the rich record costs
            // no more than a plain existence probe.
            var fileInfo = new FileInfo(resourcePath);
            if (fileInfo.Exists)
            {
                var fileResult = new StorageItemInfo(
                    Kind: StorageItemKind.File,
                    Size: fileInfo.Length,
                    ModifiedUtc: fileInfo.LastWriteTimeUtc);
                return fileResult;
            }

            var directoryInfo = new DirectoryInfo(resourcePath);
            if (directoryInfo.Exists)
            {
                var folderResult = new StorageItemInfo(
                    Kind: StorageItemKind.Folder,
                    Size: 0,
                    ModifiedUtc: directoryInfo.LastWriteTimeUtc);
                return folderResult;
            }

            var notFoundResult = new StorageItemInfo(
                Kind: StorageItemKind.NotFound,
                Size: 0,
                ModifiedUtc: default);
            return notFoundResult;
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to get info for resource: '{resource}'")
                .WithException(ex);
        }
    }

    public async Task<Result<IReadOnlyList<FolderItem>>> EnumerateFolderAsync(ResourceKey folder)
    {
        await Task.CompletedTask;

        var resolveResult = ResolvePath(folder);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{folder}'")
                .WithErrors(resolveResult);
        }
        var folderPath = resolveResult.Value;

        if (!Directory.Exists(folderPath))
        {
            return Result.Fail($"Resource is not a folder: '{folder}'");
        }

        try
        {
            // EnumerateFileSystemInfos populates each entry's metadata in the
            // single OS directory listing, avoiding a separate stat per child.
            var directoryInfo = new DirectoryInfo(folderPath);
            var entries = new List<FolderItem>();
            foreach (var info in directoryInfo.EnumerateFileSystemInfos())
            {
                var childKey = folder.Combine(info.Name);

                bool isFolder = info is DirectoryInfo;
                long size = info is FileInfo file ? file.Length : 0;

                entries.Add(new FolderItem(
                    Resource: childKey,
                    IsFolder: isFolder,
                    Size: size,
                    ModifiedUtc: info.LastWriteTimeUtc));
            }

            return entries;
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to enumerate folder: '{folder}'")
                .WithException(ex);
        }
    }

    public async Task<Result<string>> ComputeHashAsync(ResourceKey resource)
    {
        var readResult = await ReadAllBytesAsync(resource);
        if (readResult.IsFailure)
        {
            return Result.Fail($"Failed to compute hash for resource: '{resource}'")
                .WithErrors(readResult);
        }

        return FileHashHelper.HashBytes(readResult.Value);
    }

    private Result<string> ResolvePath(ResourceKey resource)
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        return resourceRegistry.ResolveResourcePath(resource);
    }

    // In production the caller always invokes IsRootWritable after ResolvePath
    // has succeeded, so a registered handler is guaranteed to be present. Unit
    // tests that stub ResolveResourcePath directly can reach this without a
    // handler in RootHandlers; treat that case as writable so those tests don't
    // need to populate the handler dictionary just to exercise writes.
    private static bool IsRootWritable(IRootHandlerRegistry rootHandlerRegistry, ResourceKey key)
    {
        return !rootHandlerRegistry.RootHandlers.TryGetValue(key.Root, out var handler)
            || handler.Capabilities.IsWritable;
    }

    // Recursive folder copy used by the chokepoint's CopyAsync path. Stays
    // internal to the FS layer so the chokepoint owns the destination structure.
    private static void CopyFolderRecursive(string sourceFolder, string destFolder)
    {
        Directory.CreateDirectory(destFolder);

        foreach (var file in Directory.GetFiles(sourceFolder))
        {
            var fileName = Path.GetFileName(file);
            var destFile = Path.Combine(destFolder, fileName);
            File.Copy(file, destFile);
        }

        foreach (var subFolder in Directory.GetDirectories(sourceFolder))
        {
            var folderName = Path.GetFileName(subFolder);
            var destSubFolder = Path.Combine(destFolder, folderName);
            CopyFolderRecursive(subFolder, destSubFolder);
        }
    }

    private async Task<Result> WriteWithRetryAsync(ResourceKey resource, byte[] bytes)
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolveResult = resourceRegistry.ResolveResourcePath(resource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{resource}'")
                .WithErrors(resolveResult);
        }
        var resourcePath = resolveResult.Value;

        var ensureParentResult = EnsureParentFolderExists(resourcePath, resource);
        if (ensureParentResult.IsFailure)
        {
            return ensureParentResult;
        }

        // Stage all in-flight temp files in <project>/.celbridge/staging-fs/.
        // Centralising them keeps user-visible folders clean of orphans after
        // a crash, and the workspace wipes the folder on load to clear any
        // stragglers from a prior session. The .celbridge folder is filtered
        // by ResourceMonitor, so no spurious watcher events fire for the
        // intermediate write.
        var stagingFolder = Path.Combine(
            resourceRegistry.ProjectFolderPath,
            ProjectConstants.CelbridgeFolder,
            ProjectConstants.StagingFsFolder);
        try
        {
            Directory.CreateDirectory(stagingFolder);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to create staging folder: '{stagingFolder}'")
                .WithException(ex);
        }

        var runResult = await FileStorageInternals.RunWithRetryAsync<bool>(
            _logger,
            operationLabel: "Write",
            resourceLabel: resource,
            resourcePath: resourcePath,
            operation: async () =>
            {
                await WriteAtomicAsync(_logger, resourcePath, stagingFolder, bytes);
                return true;
            });

        return runResult.IsSuccess ? Result.Ok() : Result.Fail(runResult);
    }

    private static Result EnsureParentFolderExists(string resourcePath, ResourceKey resource)
    {
        var parentFolder = Path.GetDirectoryName(resourcePath);
        if (string.IsNullOrEmpty(parentFolder)
            || Directory.Exists(parentFolder))
        {
            return Result.Ok();
        }

        try
        {
            Directory.CreateDirectory(parentFolder);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to create parent folder for resource: '{resource}'")
                .WithException(ex);
        }
    }

    // Writes bytes to a uniquely-named temp file inside the project's central
    // staging folder, then atomically replaces the destination via File.Move.
    // A unique filename per write prevents concurrent writers to the same
    // destination from clobbering each other's intermediate state.
    private static async Task WriteAtomicAsync(ILogger logger, string resourcePath, string stagingFolder, byte[] bytes)
    {
        var tempPath = Path.Combine(stagingFolder, Guid.NewGuid().ToString("N") + ".tmp");

        try
        {
            await File.WriteAllBytesAsync(tempPath, bytes);
            await FileStorageInternals.RetryTransientIOAsync(logger, "Atomic rename", resourcePath, () => File.Move(tempPath, resourcePath, overwrite: true));
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Best-effort cleanup. The original exception describes the real failure.
            }

            throw;
        }
    }
}
