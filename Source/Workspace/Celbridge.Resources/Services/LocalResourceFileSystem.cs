using System.Text;
using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Resources.Services;

public sealed class LocalResourceFileSystem : IResourceFileSystem
{
    private readonly ILogger<LocalResourceFileSystem> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ILocalFileSystem _fileSystem;
    private readonly SidecarCascade _sidecarCascade;
    private readonly ReferenceRewriter _referenceRewriter;

    // The resource registry is workspace-scoped and transient: a constructor-
    // injected instance is a different object from the one held by ResourceService,
    // and only the ResourceService instance has ProjectFolderPath set. The
    // file-system layer resolves the live registry through the workspace wrapper
    // at call time.
    public LocalResourceFileSystem(
        ILogger<LocalResourceFileSystem> logger,
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper,
        ILocalFileSystem fileSystem)
    {
        _logger = logger;
        _messengerService = messengerService;
        _workspaceWrapper = workspaceWrapper;
        _fileSystem = fileSystem;
        _sidecarCascade = new SidecarCascade(logger, workspaceWrapper, fileSystem);
        _referenceRewriter = new ReferenceRewriter(logger, workspaceWrapper, this, fileSystem);
    }

    public async Task<Result<byte[]>> ReadAllBytesAsync(ResourceKey resource)
    {
        var policyResult = EvaluatePolicy(resource, ResourceAction.Read, isFolder: false);
        if (policyResult.IsFailure)
        {
            return Result.Fail(policyResult);
        }

        var resolveResult = ResolvePath(resource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{resource}'")
                .WithErrors(resolveResult);
        }
        var resourcePath = resolveResult.Value;

        return await _fileSystem.ReadAllBytesAsync(resourcePath);
    }

    public async Task<Result<string>> ReadAllTextAsync(ResourceKey resource)
    {
        var policyResult = EvaluatePolicy(resource, ResourceAction.Read, isFolder: false);
        if (policyResult.IsFailure)
        {
            return Result.Fail(policyResult);
        }

        var resolveResult = ResolvePath(resource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{resource}'")
                .WithErrors(resolveResult);
        }
        var resourcePath = resolveResult.Value;

        return await _fileSystem.ReadAllTextAsync(resourcePath);
    }

    public async Task<Result<Stream>> OpenReadAsync(ResourceKey resource)
    {
        var policyResult = EvaluatePolicy(resource, ResourceAction.Read, isFolder: false);
        if (policyResult.IsFailure)
        {
            return Result.Fail(policyResult);
        }

        var resolveResult = ResolvePath(resource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{resource}'")
                .WithErrors(resolveResult);
        }
        var resourcePath = resolveResult.Value;

        return await _fileSystem.OpenReadAsync(resourcePath);
    }

    public Task<Result> WriteAllBytesAsync(ResourceKey resource, byte[] bytes)
    {
        var gateResult = EvaluateWriteDestination(resource, isFolder: false);
        if (gateResult.IsFailure)
        {
            return Task.FromResult<Result>(Result.Fail(gateResult));
        }

        return WriteBytesAsync(resource, bytes);
    }

    public Task<Result> WriteAllTextAsync(ResourceKey resource, string content)
    {
        var gateResult = EvaluateWriteDestination(resource, isFolder: false);
        if (gateResult.IsFailure)
        {
            return Task.FromResult<Result>(Result.Fail(gateResult));
        }

        var bytes = Encoding.UTF8.GetBytes(content);
        return WriteBytesAsync(resource, bytes);
    }

    public async Task<Result<Stream>> OpenWriteAsync(ResourceKey resource)
    {
        var gateResult = EvaluateWriteDestination(resource, isFolder: false);
        if (gateResult.IsFailure)
        {
            return Result.Fail(gateResult);
        }

        var resolveResult = ResolvePath(resource);
        if (resolveResult.IsFailure)
        {
            var failure = Result.Fail($"Failed to resolve path for resource: '{resource}'")
                .WithErrors(resolveResult);
            return failure;
        }
        var resourcePath = resolveResult.Value;

        var ensureParentResult = await EnsureParentFolderExistsAsync(resourcePath, resource);
        if (ensureParentResult.IsFailure)
        {
            return Result.Fail(ensureParentResult);
        }

        return await _fileSystem.OpenWriteAsync(resourcePath, WriteMode.Truncate);
    }

    public async Task<Result<MoveResult>> MoveAsync(ResourceKey source, ResourceKey dest)
    {
        if (source.Root != dest.Root)
        {
            return Result.Fail(
                $"MoveAsync requires source and destination on the same root: '{source}' to '{dest}'. " +
                "For cross-root moves, compose CopyAsync followed by DeleteAsync.");
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

        var sourceInfoResult = await _fileSystem.GetInfoAsync(sourcePath);
        if (sourceInfoResult.IsFailure)
        {
            return Result.Fail(sourceInfoResult);
        }
        var sourceInfo = sourceInfoResult.Value;

        bool sourceIsFile = sourceInfo.Kind == StorageItemKind.File;
        bool sourceIsFolder = sourceInfo.Kind == StorageItemKind.Folder;
        if (!sourceIsFile
            && !sourceIsFolder)
        {
            return Result.Fail($"Source resource does not exist: '{source}'");
        }

        // Policy runs after the kind probe so folder-only locked patterns
        // deny on the actual on-disk shape.
        var sourcePolicy = EvaluatePolicy(source, ResourceAction.Write, isFolder: sourceIsFolder);
        if (sourcePolicy.IsFailure)
        {
            return Result.Fail(sourcePolicy);
        }

        var destPolicy = EvaluateWriteDestination(dest, isFolder: sourceIsFolder);
        if (destPolicy.IsFailure)
        {
            return Result.Fail(destPolicy);
        }

        var rootHandlerRegistry = _workspaceWrapper.WorkspaceService.ResourceService.RootHandlers;
        if (!IsRootWritable(rootHandlerRegistry, dest))
        {
            return Result.Fail($"Root '{dest.Root}' is read-only.");
        }

        // Case-insensitive comparison assumes a Windows (case-insensitive) volume,
        // the project's primary target. A case-only rename (foo to Foo) resolves to
        // the same path here, so the destination-exists check below is skipped to
        // let the rename through. On a case-sensitive volume this would be Ordinal.
        bool isSameLocation = string.Equals(sourcePath, destPath, StringComparison.OrdinalIgnoreCase);
        if (!isSameLocation)
        {
            var destInfoResult = await _fileSystem.GetInfoAsync(destPath);
            if (destInfoResult.IsFailure)
            {
                return Result.Fail(destInfoResult);
            }
            if (destInfoResult.Value.Kind != StorageItemKind.NotFound)
            {
                return Result.Fail($"Destination already exists: '{dest}'");
            }
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

        // Capture descendant keys before the disk move so the post-move
        // eager-notify can drop their stale source-side index entries and
        // announce each descendant's move. After Move the source path is gone
        // and the enumeration is no longer possible.
        IReadOnlyList<ResourceKey> sourceDescendantKeys;
        if (sourceIsFolder)
        {
            sourceDescendantKeys = await EnumerateDescendantKeysAsync(rootHandlerRegistry, source, sourcePath);
        }
        else
        {
            sourceDescendantKeys = Array.Empty<ResourceKey>();
        }

        var destParent = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(destParent))
        {
            var ensureParentResult = await _fileSystem.CreateFolderAsync(destParent);
            if (ensureParentResult.IsFailure)
            {
                return Result.Fail($"Failed to create destination parent folder: '{destParent}'")
                    .WithErrors(ensureParentResult);
            }
        }

        Result moveResult;
        if (sourceIsFile)
        {
            // Clear read-only so the move itself is not blocked by an
            // attribute the user has explicitly chosen to override by
            // invoking a move on the file.
            var clearReadOnlyResult = await _fileSystem.SetAttributesAsync(sourcePath, FileSystemAttributes.ReadOnly, set: false);
            if (clearReadOnlyResult.IsFailure)
            {
                _logger.LogDebug(clearReadOnlyResult.FirstException, $"Could not clear read-only attribute before moving '{source}'");
            }
            moveResult = await _fileSystem.MoveFileAsync(sourcePath, destPath);
        }
        else
        {
            moveResult = await _fileSystem.MoveFolderAsync(sourcePath, destPath);
        }

        if (moveResult.IsFailure)
        {
            return Result.Fail($"Failed to move resource: '{source}' to '{dest}'")
                .WithErrors(moveResult);
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

        var result = new MoveResult(updatedReferencers, skippedReferencers, sidecarOutcome);
        return result;
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

        var sourceInfoResult = await _fileSystem.GetInfoAsync(sourcePath);
        if (sourceInfoResult.IsFailure)
        {
            return Result.Fail(sourceInfoResult);
        }
        var sourceInfo = sourceInfoResult.Value;

        bool sourceIsFile = sourceInfo.Kind == StorageItemKind.File;
        bool sourceIsFolder = sourceInfo.Kind == StorageItemKind.Folder;
        if (!sourceIsFile
            && !sourceIsFolder)
        {
            return Result.Fail($"Source resource does not exist: '{source}'");
        }

        var sourcePolicy = EvaluatePolicy(source, ResourceAction.Read, isFolder: sourceIsFolder);
        if (sourcePolicy.IsFailure)
        {
            return Result.Fail(sourcePolicy);
        }

        var destPolicy = EvaluateWriteDestination(dest, isFolder: sourceIsFolder);
        if (destPolicy.IsFailure)
        {
            return Result.Fail(destPolicy);
        }

        var rootHandlerRegistry = _workspaceWrapper.WorkspaceService.ResourceService.RootHandlers;
        if (!IsRootWritable(rootHandlerRegistry, dest))
        {
            return Result.Fail($"Root '{dest.Root}' is read-only.");
        }

        var destInfoResult = await _fileSystem.GetInfoAsync(destPath);
        if (destInfoResult.IsFailure)
        {
            return Result.Fail(destInfoResult);
        }
        if (destInfoResult.Value.Kind != StorageItemKind.NotFound)
        {
            return Result.Fail($"Destination already exists: '{dest}'");
        }

        var destParent = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(destParent))
        {
            var ensureParentResult = await _fileSystem.CreateFolderAsync(destParent);
            if (ensureParentResult.IsFailure)
            {
                return Result.Fail($"Failed to create destination parent folder: '{destParent}'")
                    .WithErrors(ensureParentResult);
            }
        }

        if (sourceIsFile)
        {
            var copyResult = await _fileSystem.CopyFileAsync(sourcePath, destPath);
            if (copyResult.IsFailure)
            {
                return Result.Fail($"Failed to copy resource: '{source}' to '{dest}'")
                    .WithErrors(copyResult);
            }
        }
        else
        {
            var copyResult = await CopyFolderRecursiveAsync(sourcePath, destPath);
            if (copyResult.IsFailure)
            {
                return Result.Fail($"Failed to copy resource: '{source}' to '{dest}'")
                    .WithErrors(copyResult);
            }
        }

        var sidecarOutcome = await _sidecarCascade.TryCopyAsync(source, dest);

        var result = new CopyResult(sidecarOutcome);
        return result;
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

        var sourceInfoResult = await _fileSystem.GetInfoAsync(sourcePath);
        if (sourceInfoResult.IsFailure)
        {
            return Result.Fail(sourceInfoResult);
        }
        var sourceInfo = sourceInfoResult.Value;

        bool sourceIsFile = sourceInfo.Kind == StorageItemKind.File;
        bool sourceIsFolder = sourceInfo.Kind == StorageItemKind.Folder;
        if (!sourceIsFile
            && !sourceIsFolder)
        {
            return Result.Fail($"Resource does not exist: '{source}'");
        }

        // Policy runs after the kind probe so folder-only locked patterns
        // deny on the actual on-disk shape rather than guessing.
        var policyResult = EvaluatePolicy(source, ResourceAction.Write, isFolder: sourceIsFolder);
        if (policyResult.IsFailure)
        {
            return Result.Fail(policyResult);
        }

        var rootHandlerRegistry = _workspaceWrapper.WorkspaceService.ResourceService.RootHandlers;
        if (!IsRootWritable(rootHandlerRegistry, source))
        {
            return Result.Fail($"Root '{source.Root}' is read-only.");
        }

        var sidecarOutcome = await _sidecarCascade.TryDeleteAsync(source);

        // Capture descendant keys before the disk delete so the post-delete
        // eager-notify can drop their stale index entries and announce each
        // descendant's removal too.
        IReadOnlyList<ResourceKey> descendantKeys;
        if (sourceIsFolder)
        {
            descendantKeys = await EnumerateDescendantKeysAsync(rootHandlerRegistry, source, sourcePath);
        }
        else
        {
            descendantKeys = Array.Empty<ResourceKey>();
        }

        Result deleteResult;
        if (sourceIsFile)
        {
            // Clear read-only so the delete doesn't trip on the attribute.
            // Matches OS Explorer's "delete read-only file?" behaviour
            // (proceed when the user explicitly invokes delete).
            var clearReadOnlyResult = await _fileSystem.SetAttributesAsync(sourcePath, FileSystemAttributes.ReadOnly, set: false);
            if (clearReadOnlyResult.IsFailure)
            {
                _logger.LogDebug(clearReadOnlyResult.FirstException, $"Could not clear read-only attribute before deleting '{source}'");
            }
            deleteResult = await _fileSystem.DeleteFileAsync(sourcePath);
        }
        else
        {
            // Recursive delete fails on any contained read-only file, so
            // strip the attribute throughout the subtree first.
            var deleteValidator = ResolvePathValidator(rootHandlerRegistry, source);
            await ClearReadOnlyAttributesRecursiveAsync(sourcePath, deleteValidator);
            deleteResult = await _fileSystem.DeleteFolderAsync(sourcePath, recursive: true);
        }

        if (deleteResult.IsFailure)
        {
            return Result.Fail($"Failed to delete resource: '{source}'")
                .WithErrors(deleteResult);
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

        var result = new DeleteResult(sidecarOutcome);
        return result;
    }

    // Returns the resource keys of every descendant of a folder that exists on
    // disk, both nested files and nested sub-folders. Used to capture descendant
    // keys before a recursive delete or move so eager-notify can drop their stale
    // entries from the reference index and announce each descendant's removal.
    private async Task<IReadOnlyList<ResourceKey>> EnumerateDescendantKeysAsync(IRootHandlerRegistry rootHandlerRegistry, ResourceKey folder, string folderPath)
    {
        var keys = new List<ResourceKey>();
        var validator = ResolvePathValidator(rootHandlerRegistry, folder);

        var enumerateResult = await _fileSystem.EnumerateAsync(folderPath, "*", recursive: true);
        if (enumerateResult.IsFailure)
        {
            // Best effort. A failure here just means descendant keys won't be
            // eager-notified; the watcher events still arrive eventually and
            // clean up the index.
            return keys;
        }

        foreach (var entry in enumerateResult.Value)
        {
            if (validator is not null
                && !validator(entry.FullPath))
            {
                continue;
            }
            var keyResult = rootHandlerRegistry.GetResourceKey(entry.FullPath);
            if (keyResult.IsSuccess)
            {
                keys.Add(keyResult.Value);
            }
        }

        return keys;
    }

    // Looks up the registered root handler for the key and returns its path
    // validator predicate, or null when no handler is registered. Callers that
    // walk a subtree apply it to each enumerated entry; a null predicate means
    // no entries are filtered out.
    private static Func<string, bool>? ResolvePathValidator(IRootHandlerRegistry rootHandlerRegistry, ResourceKey key)
    {
        if (rootHandlerRegistry.RootHandlers.TryGetValue(key.Root, out var handler))
        {
            return handler.PathValidator;
        }
        return null;
    }

    public async Task<Result> CreateFolderAsync(ResourceKey folder)
    {
        var gateResult = EvaluateWriteDestination(folder, isFolder: true);
        if (gateResult.IsFailure)
        {
            return Result.Fail(gateResult);
        }

        var resolveResult = ResolvePath(folder);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{folder}'")
                .WithErrors(resolveResult);
        }
        var folderPath = resolveResult.Value;

        var rootHandlerRegistry = _workspaceWrapper.WorkspaceService.ResourceService.RootHandlers;
        if (!IsRootWritable(rootHandlerRegistry, folder))
        {
            return Result.Fail($"Root '{folder.Root}' is read-only.");
        }

        var infoResult = await _fileSystem.GetInfoAsync(folderPath);
        if (infoResult.IsFailure)
        {
            return Result.Fail(infoResult);
        }
        if (infoResult.Value.Kind == StorageItemKind.File)
        {
            return Result.Fail($"Cannot create folder; a file already exists at: '{folder}'");
        }

        var createResult = await _fileSystem.CreateFolderAsync(folderPath);
        if (createResult.IsFailure)
        {
            return Result.Fail($"Failed to create folder: '{folder}'")
                .WithErrors(createResult);
        }

        return Result.Ok();
    }

    public async Task<Result<StorageItemInfo>> GetInfoAsync(ResourceKey resource)
    {
        var policyResult = EvaluatePolicy(resource, ResourceAction.Read, isFolder: false);
        if (policyResult.IsFailure)
        {
            return Result.Fail(policyResult);
        }

        var resolveResult = ResolvePath(resource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{resource}'")
                .WithErrors(resolveResult);
        }
        var resourcePath = resolveResult.Value;

        return await _fileSystem.GetInfoAsync(resourcePath);
    }

    public async Task<Result<IReadOnlyList<FolderItem>>> EnumerateFolderAsync(ResourceKey folder)
    {
        // List access to the folder itself is policy-gated; if the folder is
        // not visible, the enumeration call resolves to "no such resource"
        // semantics with a Result.Fail so callers don't mistake a denied
        // listing for an empty folder.
        if (!folder.IsEmpty)
        {
            var folderPolicy = EvaluatePolicy(folder, ResourceAction.List, isFolder: true);
            if (folderPolicy.IsFailure)
            {
                return Result.Fail(folderPolicy);
            }
        }

        // Listing skips the disk case-consistency probe: child keys below come
        // from the disk-canonical names returned by the enumeration, so they are
        // canonical regardless of the folder key's case.
        var resolveResult = ResolvePath(folder, validateCase: false);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{folder}'")
                .WithErrors(resolveResult);
        }
        var folderPath = resolveResult.Value;

        var folderInfoResult = await _fileSystem.GetInfoAsync(folderPath);
        if (folderInfoResult.IsFailure)
        {
            return Result.Fail(folderInfoResult);
        }
        if (folderInfoResult.Value.Kind != StorageItemKind.Folder)
        {
            return Result.Fail($"Resource is not a folder: '{folder}'");
        }

        var enumerateResult = await _fileSystem.EnumerateAsync(folderPath, "*", recursive: false);
        if (enumerateResult.IsFailure)
        {
            return Result.Fail($"Failed to enumerate folder: '{folder}'")
                .WithErrors(enumerateResult);
        }

        var entries = new List<FolderItem>();

        // EnumerateAsync already returns a deterministic folders-first, ordinal
        // order with size and modified-time populated from the directory walk,
        // so neither a re-sort nor a per-child stat is needed here.
        foreach (var entry in enumerateResult.Value)
        {
            var childName = Path.GetFileName(entry.FullPath);
            var childKey = folder.Combine(childName);
            if (EvaluatePolicy(childKey, ResourceAction.List, isFolder: entry.IsFolder).IsFailure)
            {
                continue;
            }

            entries.Add(new FolderItem(
                Resource: childKey,
                IsFolder: entry.IsFolder,
                Size: entry.Size,
                ModifiedUtc: entry.ModifiedUtc,
                Attributes: entry.Attributes));
        }

        IReadOnlyList<FolderItem> result = entries;
        return result.OkResult();
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

    private Result<string> ResolvePath(ResourceKey resource, bool validateCase = true)
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        return resourceRegistry.ResolveResourcePath(resource, validateCase);
    }

    // Resolves the workspace-scoped policy engine through the wrapper and
    // evaluates the supplied action against the resource. Returns Result.Ok
    // when the action is allowed, Result.Fail (with a PolicyDenialError
    // attached as an exception) when denied.
    private Result EvaluatePolicy(ResourceKey resource, ResourceAction action, bool isFolder)
    {
        var policy = _workspaceWrapper.WorkspaceService.ResourceService.Policy;
        return policy.Evaluate(resource, action, isFolder);
    }

    // Gate for the destination of a create, write, move, or copy. The Write
    // (lock) check is paired with a List (visibility) check so the operation
    // refuses to produce a resource the ignore-file would immediately hide,
    // rather than silently writing a file that never enters the registry. Raw
    // non-resource writes that intentionally land on a hidden path stay
    // available through ILocalFileSystem.
    private Result EvaluateWriteDestination(ResourceKey resource, bool isFolder)
    {
        var writeResult = EvaluatePolicy(resource, ResourceAction.Write, isFolder);
        if (writeResult.IsFailure)
        {
            return writeResult;
        }

        return EvaluatePolicy(resource, ResourceAction.List, isFolder);
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

    // Recursive folder copy used by CopyAsync. Stays here because the gateway
    // exposes only single-file copy at this layer; recursive composition is a
    // resource-layer concern.
    private async Task<Result> CopyFolderRecursiveAsync(string sourceFolder, string destFolder)
    {
        var createResult = await _fileSystem.CreateFolderAsync(destFolder);
        if (createResult.IsFailure)
        {
            return createResult;
        }

        var enumerateResult = await _fileSystem.EnumerateAsync(sourceFolder, "*", recursive: false);
        if (enumerateResult.IsFailure)
        {
            return Result.Fail(enumerateResult);
        }

        foreach (var entry in enumerateResult.Value)
        {
            if (entry.IsFolder)
            {
                continue;
            }
            var fileName = Path.GetFileName(entry.FullPath);
            var destFile = Path.Combine(destFolder, fileName);
            var copyResult = await _fileSystem.CopyFileAsync(entry.FullPath, destFile);
            if (copyResult.IsFailure)
            {
                return copyResult;
            }
        }

        foreach (var entry in enumerateResult.Value)
        {
            if (!entry.IsFolder)
            {
                continue;
            }
            var folderName = Path.GetFileName(entry.FullPath);
            var destSubFolder = Path.Combine(destFolder, folderName);
            var recursiveResult = await CopyFolderRecursiveAsync(entry.FullPath, destSubFolder);
            if (recursiveResult.IsFailure)
            {
                return recursiveResult;
            }
        }

        return Result.Ok();
    }

    // Recursive read-only clear before a recursive delete: Directory.Delete
    // fails on any contained read-only file, so strip the attribute throughout
    // the subtree first. Best-effort and silent on individual failures.
    private async Task ClearReadOnlyAttributesRecursiveAsync(string folder, Func<string, bool>? validator)
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
            if (validator is not null
                && !validator(entry.FullPath))
            {
                continue;
            }
            _ = await _fileSystem.SetAttributesAsync(entry.FullPath, FileSystemAttributes.ReadOnly, set: false);
        }
    }

    // Writes bytes directly to the destination via the ILocalFileSystem gateway.
    // No staging or atomic-replace dance: the gateway's retry policy absorbs
    // transient sharing violations, and a crash mid-write leaves the file
    // truncated for that single resource (acceptable for a desktop editor where
    // saves run continuously and the in-memory buffer is the source of truth).
    // Watchers see a single Changed event per save.
    private async Task<Result> WriteBytesAsync(ResourceKey resource, byte[] bytes)
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolveResult = resourceRegistry.ResolveResourcePath(resource);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for resource: '{resource}'")
                .WithErrors(resolveResult);
        }
        var resourcePath = resolveResult.Value;

        var ensureParentResult = await EnsureParentFolderExistsAsync(resourcePath, resource);
        if (ensureParentResult.IsFailure)
        {
            return ensureParentResult;
        }

        // Only strip the on-disk read-only attribute when the writable cache
        // confirms the resource is writable. If the cache reports any non-
        // writable state, leave the attribute in place and let the underlying
        // write fail. This stops a stale attribute (cleared by an external
        // tool but not yet seen by the watcher) from blocking a legitimate
        // save, while also stopping auto-save from clobbering a read-only
        // mark the user just applied.
        var operationService = _workspaceWrapper.WorkspaceService.ResourceService.Operations;
        var writableState = await operationService.GetWritableStateAsync(resource);
        if (writableState == WritableState.Writable)
        {
            var clearReadOnlyResult = await _fileSystem.SetAttributesAsync(resourcePath, FileSystemAttributes.ReadOnly, set: false);
            if (clearReadOnlyResult.IsFailure)
            {
                _logger.LogDebug(clearReadOnlyResult.FirstException, $"Could not clear read-only attribute before writing '{resource}'");
            }
        }

        var writeResult = await _fileSystem.WriteAllBytesAsync(resourcePath, bytes);
        if (writeResult.IsFailure)
        {
            return Result.Fail($"Failed to write file: '{resource}'")
                .WithErrors(writeResult);
        }

        return Result.Ok();
    }

    private async Task<Result> EnsureParentFolderExistsAsync(string resourcePath, ResourceKey resource)
    {
        var parentFolder = Path.GetDirectoryName(resourcePath);
        if (string.IsNullOrEmpty(parentFolder))
        {
            return Result.Ok();
        }

        var infoResult = await _fileSystem.GetInfoAsync(parentFolder);
        if (infoResult.IsSuccess
            && infoResult.Value.Kind == StorageItemKind.Folder)
        {
            return Result.Ok();
        }

        var createResult = await _fileSystem.CreateFolderAsync(parentFolder);
        if (createResult.IsFailure)
        {
            return Result.Fail($"Failed to create parent folder for resource: '{resource}'")
                .WithErrors(createResult);
        }

        return Result.Ok();
    }
}
