using System.Text;
using Celbridge.Logging;
using Celbridge.Resources.Helpers;
using Celbridge.Workspace;

namespace Celbridge.Resources.Services;

public sealed class LocalResourceFileSystem : IResourceFileSystem
{
    private readonly ILogger<LocalResourceFileSystem> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ILocalFileSystem _fileSystem;
    private readonly SidecarCascade _sidecarCascade;

    // The root handler registry belongs to the ResourceService, which is created
    // after this service. The gateway reaches it through the workspace wrapper at
    // call time.
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

        var openResult = await _fileSystem.OpenWriteAsync(resourcePath, WriteMode.Truncate);
        if (openResult.IsSuccess)
        {
            // The writes through the stream come after the report, and the watcher reports them.
            using var changes = CreateChangeRecorder();
            changes.RecordCreated(resource, resourcePath);
        }

        return openResult;
    }

    public async Task<Result<MoveResult>> MoveAsync(ResourceKey source, ResourceKey dest, MoveOptions? options = null)
    {
        // Keys only make sense within a root. Moving a resource to another root deletes the resource from one root
        // and creates it in the other, so no key changed message is sent. The caller has to allow such a move by
        // setting MoveOptions.AllowCrossRoot.
        var isCrossRoot = source.Root != dest.Root;
        if (isCrossRoot
            && options?.AllowCrossRoot != true)
        {
            return Result.Fail(
                $"MoveAsync requires source and destination on the same root: '{source}' to '{dest}'. " +
                "Pass MoveOptions with AllowCrossRoot for a move that crosses roots, or compose " +
                "CopyAsync followed by DeleteAsync.");
        }

        var rootHandlerRegistry = _workspaceWrapper.WorkspaceService.ResourceService.RootHandlers;

        var resolveSourceResult = rootHandlerRegistry.ResolveResourcePath(source);
        if (resolveSourceResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for source resource: '{source}'")
                .WithErrors(resolveSourceResult);
        }
        var sourcePath = resolveSourceResult.Value;

        string destPath;
        if (LetterCaseHelper.DiffersOnlyInNameCase(source, dest))
        {
            // The item is still stored under the source's name, so resolving the destination would refuse its
            // letter case. The destination's path is the source's path with the new name.
            destPath = Path.Combine(Path.GetDirectoryName(sourcePath)!, dest.ResourceName);
        }
        else
        {
            var resolveDestResult = rootHandlerRegistry.ResolveResourcePath(dest);
            if (resolveDestResult.IsFailure)
            {
                return Result.Fail($"Failed to resolve path for destination resource: '{dest}'")
                    .WithErrors(resolveDestResult);
            }
            destPath = resolveDestResult.Value;
        }

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

        if (!IsRootWritable(rootHandlerRegistry, source))
        {
            return Result.Fail($"Root '{source.Root}' is read-only.");
        }

        if (!IsRootWritable(rootHandlerRegistry, dest))
        {
            return Result.Fail($"Root '{dest.Root}' is read-only.");
        }

        // A rename that changes only letter case keeps the item in place, so the destination exists already.
        bool isSameLocation = string.Equals(sourcePath, destPath, PathComparison.Comparison);
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

        // A rename that changes only letter case keeps the item in place. The old name leaves the listing before
        // the new name joins it.
        using var changes = CreateChangeRecorder();
        changes.RecordRemoved(source, sourcePath);
        changes.RecordCreated(dest, destPath);

        var sidecarOutcome = await _sidecarCascade.TryMoveAsync(source, dest, changes);

        // Send one message for the move, before the report. Each receiver maps the keys inside a moved folder.
        if (!isCrossRoot)
        {
            var keyChangedMessage = new ResourceKeyChangedMessage(source, dest);
            _messengerService.Send(keyChangedMessage);
        }

        var result = new MoveResult(sidecarOutcome);
        return result;
    }

    public async Task<Result<CopyResult>> CopyAsync(ResourceKey source, ResourceKey dest)
    {
        var rootHandlerRegistry = _workspaceWrapper.WorkspaceService.ResourceService.RootHandlers;

        var resolveSourceResult = rootHandlerRegistry.ResolveResourcePath(source);
        if (resolveSourceResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve path for source resource: '{source}'")
                .WithErrors(resolveSourceResult);
        }
        var sourcePath = resolveSourceResult.Value;

        var resolveDestResult = rootHandlerRegistry.ResolveResourcePath(dest);
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

        using var changes = CreateChangeRecorder();
        changes.RecordCreated(dest, destPath);

        var sidecarOutcome = await _sidecarCascade.TryCopyAsync(source, dest, changes);

        var result = new CopyResult(sidecarOutcome);
        return result;
    }

    public async Task<Result<DeleteResult>> DeleteAsync(ResourceKey source)
    {
        var rootHandlerRegistry = _workspaceWrapper.WorkspaceService.ResourceService.RootHandlers;

        var resolveResult = rootHandlerRegistry.ResolveResourcePath(source);
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

        if (!IsRootWritable(rootHandlerRegistry, source))
        {
            return Result.Fail($"Root '{source.Root}' is read-only.");
        }

        using var changes = CreateChangeRecorder();
        var sidecarOutcome = await _sidecarCascade.TryDeleteAsync(source, changes);

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

        changes.RecordRemoved(source, sourcePath);

        var result = new DeleteResult(sidecarOutcome);
        return result;
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

        using var changes = CreateChangeRecorder();
        changes.RecordCreated(folder, folderPath);

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

    public async Task<Result<ResourceItem>> GetResourceItemAsync(ResourceKey resource)
    {
        var infoResult = await GetInfoAsync(resource);
        if (infoResult.IsFailure)
        {
            return Result.Fail($"Failed to read resource: '{resource}'")
                .WithErrors(infoResult);
        }

        var itemKind = infoResult.Value.Kind;
        if (itemKind == StorageItemKind.File)
        {
            return new ResourceItem(resource, IsFolder: false);
        }

        if (itemKind == StorageItemKind.Folder)
        {
            return new ResourceItem(resource, IsFolder: true);
        }

        return Result.Fail($"Resource not found: '{resource}'");
    }

    public async Task<Result<IReadOnlyList<FolderChild>>> EnumerateFolderAsync(ResourceKey folder)
    {
        var resolveResult = await ResolveFolderToListAsync(folder);
        if (resolveResult.IsFailure)
        {
            return Result.Fail(resolveResult);
        }
        var folderPath = resolveResult.Value;

        var enumerateResult = await _fileSystem.EnumerateAsync(folderPath, "*", recursive: false);
        if (enumerateResult.IsFailure)
        {
            return Result.Fail($"Failed to enumerate folder: '{folder}'")
                .WithErrors(enumerateResult);
        }

        var entries = new List<FolderChild>();

        // EnumerateAsync already returns a deterministic folders-first, ordinal
        // order. Size and modified time come from the directory walk, so the
        // listing needs no re-sort and no stat per child.
        foreach (var entry in enumerateResult.Value)
        {
            var folderChild = ToFolderChild(folder, entry);
            if (folderChild is not null)
            {
                entries.Add(folderChild);
            }
        }

        IReadOnlyList<FolderChild> result = entries;
        return result.OkResult();
    }

    public async Task<Result<bool>> HasChildAsync(ResourceKey folder, Func<FolderChild, bool> predicate)
    {
        var resolveResult = await ResolveFolderToListAsync(folder);
        if (resolveResult.IsFailure)
        {
            return Result.Fail(resolveResult);
        }
        var folderPath = resolveResult.Value;

        var hasEntryResult = await _fileSystem.HasEntryAsync(folderPath, entry =>
        {
            var folderChild = ToFolderChild(folder, entry);
            return folderChild is not null
                && predicate(folderChild);
        });
        if (hasEntryResult.IsFailure)
        {
            return Result.Fail($"Failed to enumerate folder: '{folder}'")
                .WithErrors(hasEntryResult);
        }

        return hasEntryResult.Value;
    }

    // Returns the path of a folder to list. Fails for a reserved folder, for a key that does not resolve, and for a
    // key that names something other than a folder.
    private async Task<Result<string>> ResolveFolderToListAsync(ResourceKey folder)
    {
        // A reserved folder resolves to "no such resource" semantics with a
        // Result.Fail so callers don't mistake a denied listing for an empty
        // folder.
        if (!folder.IsEmpty)
        {
            var folderPolicy = EvaluatePolicy(folder, ResourceAction.Read, isFolder: true);
            if (folderPolicy.IsFailure)
            {
                return Result.Fail(folderPolicy);
            }
        }

        var resolveResult = ResolvePath(folder);
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

        return folderPath;
    }

    // Returns the child an entry of the folder describes, or null when listings leave the entry out.
    private FolderChild? ToFolderChild(ResourceKey folder, FileSystemEntry entry)
    {
        var childName = Path.GetFileName(entry.FullPath);
        var childKey = folder.Combine(childName);

        // Enumeration drops the reserved folders and links. The project's
        // hide patterns are a view filter the Explorer applies, so a hidden entry
        // is still listed here.
        if (EvaluatePolicy(childKey, ResourceAction.Read, isFolder: entry.IsFolder).IsFailure)
        {
            return null;
        }

        // The resolver refuses a link and every path through one. A listing that
        // offered one would hand the caller an item it cannot open.
        if (entry.IsLink)
        {
            return null;
        }

        return new FolderChild(
            Resource: childKey,
            IsFolder: entry.IsFolder,
            Size: entry.Size,
            ModifiedUtc: entry.ModifiedUtc,
            Attributes: entry.Attributes);
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

    public async Task<Result> SetAttributesAsync(ResourceKey resource, FileSystemAttributes mask, bool set)
    {
        var infoResult = await GetInfoAsync(resource);
        if (infoResult.IsFailure)
        {
            return Result.Fail($"Failed to probe resource for attribute change: '{resource}'")
                .WithErrors(infoResult);
        }

        var policyResult = EvaluatePolicy(resource, ResourceAction.Write, isFolder: infoResult.Value.Kind == StorageItemKind.Folder);
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

        var setResult = await _fileSystem.SetAttributesAsync(resolveResult.Value, mask, set);
        if (setResult.IsSuccess)
        {
            using var changes = CreateChangeRecorder();
            changes.RecordChanged(resource);
        }

        return setResult;
    }

    private Result<string> ResolvePath(ResourceKey resource)
    {
        var rootHandlerRegistry = _workspaceWrapper.WorkspaceService.ResourceService.RootHandlers;
        return rootHandlerRegistry.ResolveResourcePath(resource);
    }

    private ChangeRecorder CreateChangeRecorder()
    {
        var resourceService = _workspaceWrapper.WorkspaceService.ResourceService;
        return new ChangeRecorder(resourceService);
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

    // Gate for the destination of a create, write, move, or copy. Raw
    // non-resource writes that intentionally land on a reserved path stay
    // available through ILocalFileSystem.
    private Result EvaluateWriteDestination(ResourceKey resource, bool isFolder)
    {
        return EvaluatePolicy(resource, ResourceAction.Write, isFolder);
    }

    // Returns false for a root with no handler.
    private static bool IsRootWritable(IRootHandlerRegistry rootHandlerRegistry, ResourceKey key)
    {
        return rootHandlerRegistry.RootHandlers.TryGetValue(key.Root, out var handler)
            && handler.Capabilities.IsWritable;
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

        // A copy would replace a link with what it points at, so links are left out.
        var entries = enumerateResult.Value
            .Where(entry => !entry.IsLink)
            .ToList();

        foreach (var entry in entries)
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

        foreach (var entry in entries)
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
        var rootHandlerRegistry = _workspaceWrapper.WorkspaceService.ResourceService.RootHandlers;

        var resolveResult = rootHandlerRegistry.ResolveResourcePath(resource);
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

        var writeResult = await _fileSystem.WriteAllBytesAsync(resourcePath, bytes);
        if (writeResult.IsFailure)
        {
            return Result.Fail($"Failed to write file: '{resource}'")
                .WithErrors(writeResult);
        }

        using var changes = CreateChangeRecorder();
        changes.RecordCreated(resource, resourcePath);

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
