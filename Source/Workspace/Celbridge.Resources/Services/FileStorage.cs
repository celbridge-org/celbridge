using System.Text;
using Celbridge.Logging;
using Celbridge.Projects;
using Celbridge.Resources.Helpers;
using Celbridge.Utilities;
using Celbridge.Workspace;

namespace Celbridge.Resources.Services;

public sealed class FileStorage : IFileStorage
{
    // Bounded retry for transient IO failures (file briefly locked by AV,
    // backup software, sync clients, concurrent writers, etc.). Total
    // worst-case wait across all attempts is BaseRetryDelayMs * (1 + 2 + ...
    // + (MaxAttempts - 1)) = 150ms with the values below.
    private const int MaxAttempts = 3;
    private const int BaseRetryDelayMs = 50;

    // Buffer size used when opening file streams. Matches the default System.IO
    // FileStream buffer size when none is supplied.
    private const int StreamBufferSize = 4096;

    private readonly ILogger<FileStorage> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

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
    }

    public async Task<Result<byte[]>> ReadAllBytesAsync(ResourceKey resource)
    {
        var resolveResult = ResolvePath(resource);
        if (resolveResult.IsFailure)
        {
            return Result<byte[]>.Fail($"Failed to resolve path for resource: '{resource}'")
                .WithErrors(resolveResult);
        }
        var resourcePath = resolveResult.Value;

        return await RunWithRetryAsync<byte[]>(
            operationLabel: "Read",
            resource: resource,
            resourcePath: resourcePath,
            operation: () => File.ReadAllBytesAsync(resourcePath),
            shouldRetry: IsTransientReadIOException);
    }

    public async Task<Result<string>> ReadAllTextAsync(ResourceKey resource)
    {
        var resolveResult = ResolvePath(resource);
        if (resolveResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to resolve path for resource: '{resource}'")
                .WithErrors(resolveResult);
        }
        var resourcePath = resolveResult.Value;

        return await RunWithRetryAsync<string>(
            operationLabel: "Read",
            resource: resource,
            resourcePath: resourcePath,
            operation: () => File.ReadAllTextAsync(resourcePath),
            shouldRetry: IsTransientReadIOException);
    }

    public async Task<Result<Stream>> OpenReadAsync(ResourceKey resource)
    {
        var resolveResult = ResolvePath(resource);
        if (resolveResult.IsFailure)
        {
            return Result<Stream>.Fail($"Failed to resolve path for resource: '{resource}'")
                .WithErrors(resolveResult);
        }
        var resourcePath = resolveResult.Value;

        return await RunWithRetryAsync<Stream>(
            operationLabel: "Read",
            resource: resource,
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
            var failure = Result<Stream>.Fail($"Failed to resolve path for resource: '{resource}'")
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
            var failure = Result<Stream>.Fail($"Failed to open write stream for resource: '{resource}'")
                .WithException(ex);
            return failure;
        }
    }

    public async Task<Result<MoveResult>> MoveAsync(ResourceKey source, ResourceKey destination)
    {
        if (source.Root != destination.Root)
        {
            return Result<MoveResult>.Fail($"Cross-root move not supported: '{source}' to '{destination}'");
        }

        var registry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolveSourceResult = registry.ResolveResourcePath(source);
        if (resolveSourceResult.IsFailure)
        {
            return Result<MoveResult>.Fail($"Failed to resolve path for source resource: '{source}'")
                .WithErrors(resolveSourceResult);
        }
        var sourcePath = resolveSourceResult.Value;

        var resolveDestResult = registry.ResolveResourcePath(destination);
        if (resolveDestResult.IsFailure)
        {
            return Result<MoveResult>.Fail($"Failed to resolve path for destination resource: '{destination}'")
                .WithErrors(resolveDestResult);
        }
        var destPath = resolveDestResult.Value;

        bool sourceIsFile = File.Exists(sourcePath);
        bool sourceIsFolder = Directory.Exists(sourcePath);
        if (!sourceIsFile
            && !sourceIsFolder)
        {
            return Result<MoveResult>.Fail($"Source resource does not exist: '{source}'");
        }

        var rootHandlerRegistry = _workspaceWrapper.WorkspaceService.ResourceService.RootHandlerRegistry;
        if (!IsRootWritable(rootHandlerRegistry, destination))
        {
            return Result<MoveResult>.Fail($"Root '{destination.Root}' is read-only.");
        }

        bool isSameLocation = string.Equals(sourcePath, destPath, StringComparison.OrdinalIgnoreCase);
        if (!isSameLocation
            && (File.Exists(destPath) || Directory.Exists(destPath)))
        {
            return Result<MoveResult>.Fail($"Destination already exists: '{destination}'");
        }

        var updatedReferencers = new List<ResourceKey>();
        var skippedReferencers = new List<SkippedReferencer>();

        if (source.Root == ResourceKey.DefaultRoot)
        {
            var rewriteResult = await RewriteReferencesForMoveAsync(source, destination, sourceIsFolder, updatedReferencers, skippedReferencers);
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
                ClearReadOnlyIfSet(sourcePath);
                await FileSystemHelper.MoveWithRetryAsync(() => File.Move(sourcePath, destPath));
            }
            else
            {
                await FileSystemHelper.MoveWithRetryAsync(() => Directory.Move(sourcePath, destPath));
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result<MoveResult>.Fail($"Failed to move resource '{source}' to '{destination}': access denied (permissions or file in use).")
                .WithException(ex);
        }
        catch (Exception ex)
        {
            return Result<MoveResult>.Fail($"Failed to move resource: '{source}' to '{destination}'")
                .WithException(ex);
        }

        var sidecarOutcome = await TryCascadeSidecarMoveAsync(source, destination);

        if (source.Root == ResourceKey.DefaultRoot)
        {
            // Announce the source removal synchronously so subscribers update
            // before control returns. The watcher's own delete event still
            // arrives later via UI-thread dispatch; subscribers must treat
            // these messages as idempotent.
            var sourceRemovedMessage = new ResourceDeletedMessage(source);
            _messengerService.Send(sourceRemovedMessage);
            foreach (var key in sourceDescendantKeys)
            {
                var descendantRemovedMessage = new ResourceDeletedMessage(key);
                _messengerService.Send(descendantRemovedMessage);
            }
        }

        await Task.CompletedTask;

        var moveResult = new MoveResult(updatedReferencers, skippedReferencers, sidecarOutcome);
        return moveResult;
    }

    public async Task<Result<CopyResult>> CopyAsync(ResourceKey source, ResourceKey destination)
    {
        var registry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolveSourceResult = registry.ResolveResourcePath(source);
        if (resolveSourceResult.IsFailure)
        {
            return Result<CopyResult>.Fail($"Failed to resolve path for source resource: '{source}'")
                .WithErrors(resolveSourceResult);
        }
        var sourcePath = resolveSourceResult.Value;

        var resolveDestResult = registry.ResolveResourcePath(destination);
        if (resolveDestResult.IsFailure)
        {
            return Result<CopyResult>.Fail($"Failed to resolve path for destination resource: '{destination}'")
                .WithErrors(resolveDestResult);
        }
        var destPath = resolveDestResult.Value;

        bool sourceIsFile = File.Exists(sourcePath);
        bool sourceIsFolder = Directory.Exists(sourcePath);
        if (!sourceIsFile
            && !sourceIsFolder)
        {
            return Result<CopyResult>.Fail($"Source resource does not exist: '{source}'");
        }

        var rootHandlerRegistry = _workspaceWrapper.WorkspaceService.ResourceService.RootHandlerRegistry;
        if (!IsRootWritable(rootHandlerRegistry, destination))
        {
            return Result<CopyResult>.Fail($"Root '{destination.Root}' is read-only.");
        }

        if (File.Exists(destPath)
            || Directory.Exists(destPath))
        {
            return Result<CopyResult>.Fail($"Destination already exists: '{destination}'");
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
                File.Copy(sourcePath, destPath);
            }
            else
            {
                CopyFolderRecursive(sourcePath, destPath);
            }
        }
        catch (Exception ex)
        {
            return Result<CopyResult>.Fail($"Failed to copy resource: '{source}' to '{destination}'")
                .WithException(ex);
        }

        var sidecarOutcome = TryCascadeSidecarCopy(source, destination);

        await Task.CompletedTask;

        var copyResult = new CopyResult(sidecarOutcome);
        return copyResult;
    }

    public async Task<Result<DeleteResult>> DeleteAsync(ResourceKey source)
    {
        var registry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolveResult = registry.ResolveResourcePath(source);
        if (resolveResult.IsFailure)
        {
            return Result<DeleteResult>.Fail($"Failed to resolve path for resource: '{source}'")
                .WithErrors(resolveResult);
        }
        var sourcePath = resolveResult.Value;

        bool sourceIsFile = File.Exists(sourcePath);
        bool sourceIsFolder = Directory.Exists(sourcePath);
        if (!sourceIsFile
            && !sourceIsFolder)
        {
            return Result<DeleteResult>.Fail($"Resource does not exist: '{source}'");
        }

        var rootHandlerRegistry = _workspaceWrapper.WorkspaceService.ResourceService.RootHandlerRegistry;
        if (!IsRootWritable(rootHandlerRegistry, source))
        {
            return Result<DeleteResult>.Fail($"Root '{source.Root}' is read-only.");
        }

        var sidecarOutcome = TryCascadeSidecarDelete(source);

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
                ClearReadOnlyIfSet(sourcePath);
                File.Delete(sourcePath);
            }
            else
            {
                // Recursive delete fails on any contained read-only file, so
                // strip the attribute throughout the subtree first.
                ClearReadOnlyRecursive(sourcePath);
                Directory.Delete(sourcePath, recursive: true);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result<DeleteResult>.Fail($"Failed to delete resource '{source}': access denied (permissions or file in use).")
                .WithException(ex);
        }
        catch (Exception ex)
        {
            return Result<DeleteResult>.Fail($"Failed to delete resource: '{source}'")
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

        await Task.CompletedTask;

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

    public async Task<Result<StorageItemInfo>> GetInfoAsync(ResourceKey resource)
    {
        await Task.CompletedTask;

        var resolveResult = ResolvePath(resource);
        if (resolveResult.IsFailure)
        {
            return Result<StorageItemInfo>.Fail($"Failed to resolve path for resource: '{resource}'")
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
            return Result<StorageItemInfo>.Fail($"Failed to get info for resource: '{resource}'")
                .WithException(ex);
        }
    }

    public async Task<Result<IReadOnlyList<FolderItem>>> EnumerateFolderAsync(ResourceKey folder)
    {
        await Task.CompletedTask;

        var resolveResult = ResolvePath(folder);
        if (resolveResult.IsFailure)
        {
            return Result<IReadOnlyList<FolderItem>>.Fail($"Failed to resolve path for resource: '{folder}'")
                .WithErrors(resolveResult);
        }
        var folderPath = resolveResult.Value;

        if (!Directory.Exists(folderPath))
        {
            return Result<IReadOnlyList<FolderItem>>.Fail($"Resource is not a folder: '{folder}'");
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

            return Result<IReadOnlyList<FolderItem>>.Ok(entries);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<FolderItem>>.Fail($"Failed to enumerate folder: '{folder}'")
                .WithException(ex);
        }
    }

    public async Task<Result<string>> ComputeHashAsync(ResourceKey resource)
    {
        var readResult = await ReadAllBytesAsync(resource);
        if (readResult.IsFailure)
        {
            return Result<string>.Fail($"Failed to compute hash for resource: '{resource}'")
                .WithErrors(readResult);
        }

        return FileHashHelper.HashBytes(readResult.Value);
    }

    private Result<string> ResolvePath(ResourceKey resource)
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        return resourceRegistry.ResolveResourcePath(resource);
    }

    // Roots that don't have a registered handler are assumed writable — the
    // default project root falls into this category and is always writable.
    private static bool IsRootWritable(IRootHandlerRegistry rootHandlerRegistry, ResourceKey key)
    {
        return !rootHandlerRegistry.RootHandlers.TryGetValue(key.Root, out var handler)
            || handler.Capabilities.IsWritable;
    }

    // Re-writes every "project:<source>" literal in every referencer of source
    // (and, for folders, every "project:<source>/<rest>" literal). The rewrite is
    // performed via this layer's own ReadAllTextAsync / WriteAllTextAsync so the
    // atomic-write semantics apply to each touched file. On any failure the
    // operation aborts; previously-rewritten files are left at their new state
    // and the source bytes are still in place, so a re-run completes the work.
    private async Task<Result> RewriteReferencesForMoveAsync(
        ResourceKey source,
        ResourceKey destination,
        bool sourceIsFolder,
        List<ResourceKey> updatedReferencers,
        List<SkippedReferencer> skippedReferencers)
    {
        var scanner = _workspaceWrapper.WorkspaceService.ResourceScanner;

        var referencerSet = new HashSet<ResourceKey>();
        foreach (var referencer in await scanner.FindReferencersAsync(source))
        {
            referencerSet.Add(referencer);
        }

        if (sourceIsFolder)
        {
            // Children of source contribute prefix-form references; gather every
            // referencer of every descendant target so the prefix rewrite reaches
            // each file that names a child key.
            foreach (var target in await scanner.FindAllReferencedTargetsAsync())
            {
                if (target.IsDescendantOf(source))
                {
                    foreach (var referencer in await scanner.FindReferencersAsync(target))
                    {
                        referencerSet.Add(referencer);
                    }
                }
            }
        }

        var sourceLiteral = source.FullKey;
        var destLiteral = destination.FullKey;

        var orderedReferencers = referencerSet
            .OrderBy(r => r.ToString(), StringComparer.Ordinal)
            .ToList();

        // Per-referencer failures (typically file locked by an external editor
        // for a moment, or marked read-only by the user) are logged and skipped
        // rather than aborting the whole move. The parent move still completes;
        // data_check_project surfaces any references that remained stale, and a
        // subsequent rerun of the rename picks up the residual rewrites because
        // the FS layer is idempotent under partial completion (the source bytes
        // are still in place between the rewrite loop and the parent move, and
        // the next scanner call re-derives the referencer set).
        foreach (var referencer in orderedReferencers)
        {
            var readResult = await ReadAllTextAsync(referencer);
            if (readResult.IsFailure)
            {
                var message = $"read failed for '{referencer}'";
                _logger.LogWarning($"Could not rewrite references in '{referencer}' for rename of '{source}' to '{destination}': {message}. The reference is left as-is and will surface via data_check_project.");
                skippedReferencers.Add(new SkippedReferencer(referencer, ReferencerSkipReason.ReadFailed, message));
                continue;
            }
            var originalText = readResult.Value;

            var rewritten = RewriteReferenceLiterals(originalText, sourceLiteral, destLiteral, sourceIsFolder);
            if (rewritten == originalText)
            {
                continue;
            }

            // Honor the DOS read-only attribute as a "do not modify" hint
            // BEFORE attempting the write. The atomic temp+rename path would
            // surface this as a write failure on Windows (MoveFileEx checks
            // the target's read-only bit) but silently succeed on Linux
            // (rename only checks write permission on the parent directory,
            // not on the target). Pre-checking closes that cross-platform
            // gap so the user's "don't touch this file" intent is honored
            // identically on every platform.
            if (IsReferencerReadOnly(referencer))
            {
                const string readOnlyMessage = "file is read-only";
                _logger.LogWarning($"Could not rewrite references in '{referencer}' for rename of '{source}' to '{destination}': {readOnlyMessage}. The reference is left as-is and will surface via data_check_project.");
                skippedReferencers.Add(new SkippedReferencer(referencer, ReferencerSkipReason.ReadOnly, readOnlyMessage));
                continue;
            }

            var writeResult = await WriteAllTextAsync(referencer, rewritten);
            if (writeResult.IsFailure)
            {
                // The referencer cascade does not override user-set read-only
                // or ACL permissions: the user invoked a move on `source`, not
                // on this incidental referencer. Skip with a clear message so
                // the user (or the calling agent) knows exactly why and can
                // decide whether to fix the permissions and rerun the rename.
                var classification = ClassifyReferencerWriteFailure(referencer, writeResult);
                _logger.LogWarning($"Could not rewrite references in '{referencer}' for rename of '{source}' to '{destination}': {classification.Message}. The reference is left as-is and will surface via data_check_project.");
                skippedReferencers.Add(new SkippedReferencer(referencer, classification.Reason, classification.Message));
                continue;
            }

            updatedReferencers.Add(referencer);
        }

        return Result.Ok();
    }

    private bool IsReferencerReadOnly(ResourceKey referencer)
    {
        var resolveResult = ResolvePath(referencer);
        if (resolveResult.IsFailure)
        {
            return false;
        }

        try
        {
            var info = new FileInfo(resolveResult.Value);
            return info.Exists
                && info.IsReadOnly;
        }
        catch
        {
            return false;
        }
    }

    private (ReferencerSkipReason Reason, string Message) ClassifyReferencerWriteFailure(ResourceKey referencer, Result writeResult)
    {
        var resolveResult = ResolvePath(referencer);
        if (resolveResult.IsFailure)
        {
            return (ReferencerSkipReason.WriteFailed, "write failed (could not resolve path)");
        }

        // Check the DOS read-only attribute first. We split it from the ACL /
        // POSIX denial case because the fixes are different: read-only is
        // trivially clearable ("uncheck the read-only flag"), whereas an ACL
        // deny typically needs the right user account or admin rights. Agents
        // that want to auto-clear read-only-and-retry can switch on ReadOnly;
        // agents that want a coarse "permissions thing" check can match both.
        try
        {
            var info = new FileInfo(resolveResult.Value);
            if (info.Exists
                && info.IsReadOnly)
            {
                return (ReferencerSkipReason.ReadOnly, "file is read-only");
            }
        }
        catch
        {
            // Fall through to the exception-based classification.
        }

        // UnauthorizedAccessException from the underlying File.Move (after the
        // atomic temp-write) typically means an ACL deny on Windows or a POSIX
        // permission failure on Unix.
        if (writeResult.FirstException is UnauthorizedAccessException)
        {
            return (ReferencerSkipReason.PermissionDenied, "permission denied (no write access to file)");
        }

        // Catch-all for any other write failure: actual file locks, disk full,
        // quota exceeded, network share gone, antivirus interference. The
        // hedged message tells the user where to look without overcommitting
        // to a specific cause we can't detect.
        return (ReferencerSkipReason.WriteFailed, "write failed (file may be locked or another IO issue)");
    }

    // Replaces every quoted occurrence of sourceLiteral with destLiteral. The
    // boundary check (ResourceReferenceParser.IsNonKeyBoundary on the bytes
    // immediately before and after the match) keeps incidental substring
    // matches untouched — only the canonical quoted form gets rewritten.
    //
    // Both sides of the match must have a real boundary character; matches at
    // position 0 or at end-of-text are not eligible because under the
    // always-quoted contract every tracked reference is wrapped in a quote
    // (or its \" / \' escape) on both sides.
    //
    // Folder cascade: the trailing-boundary check also accepts '/' so a folder
    // rename rewrites descendant references via prefix substitution —
    // "project:<folder>/<child>" becomes "project:<newfolder>/<child>" because
    // sourceLiteral matched only the "<folder>" prefix.
    private static string RewriteReferenceLiterals(string text, string sourceLiteral, string destLiteral, bool sourceIsFolder)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        int cursor = 0;

        while (cursor < text.Length)
        {
            int matchIndex = text.IndexOf(sourceLiteral, cursor, StringComparison.Ordinal);
            if (matchIndex < 0)
            {
                builder.Append(text, cursor, text.Length - cursor);
                break;
            }

            builder.Append(text, cursor, matchIndex - cursor);

            int afterMatch = matchIndex + sourceLiteral.Length;

            bool leadingOk = matchIndex > 0
                && ResourceReferenceParser.IsNonKeyBoundary(text[matchIndex - 1]);
            bool trailingExact = afterMatch < text.Length
                && ResourceReferenceParser.IsNonKeyBoundary(text[afterMatch]);
            bool trailingFolderPrefix = sourceIsFolder
                && afterMatch < text.Length
                && text[afterMatch] == '/';

            if (leadingOk
                && (trailingExact || trailingFolderPrefix))
            {
                builder.Append(destLiteral);
                cursor = afterMatch;
            }
            else
            {
                // Boundary check failed. Preserve the matched byte and advance
                // by one so the next scan can find an overlapping occurrence.
                builder.Append(text[matchIndex]);
                cursor = matchIndex + 1;
            }
        }

        return builder.ToString();
    }

    private async Task<SidecarOutcome> TryCascadeSidecarMoveAsync(ResourceKey source, ResourceKey destination)
    {
        var sourceSidecar = AppendSidecarSuffix(source);
        var destSidecar = AppendSidecarSuffix(destination);
        if (sourceSidecar is null
            || destSidecar is null)
        {
            return SidecarOutcome.NotPresent;
        }

        var registry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolveSourceResult = registry.ResolveResourcePath(sourceSidecar.Value);
        if (resolveSourceResult.IsFailure)
        {
            return SidecarOutcome.NotPresent;
        }
        var sourceSidecarPath = resolveSourceResult.Value;
        if (!File.Exists(sourceSidecarPath))
        {
            return SidecarOutcome.NotPresent;
        }

        var resolveDestResult = registry.ResolveResourcePath(destSidecar.Value);
        if (resolveDestResult.IsFailure)
        {
            _logger.LogWarning($"Failed to resolve sidecar destination '{destSidecar}' for move from '{source}'. Sidecar bytes remain at the source path.");
            return SidecarOutcome.Failed;
        }
        var destSidecarPath = resolveDestResult.Value;

        if (File.Exists(destSidecarPath))
        {
            _logger.LogWarning($"Sidecar destination '{destSidecar}' already exists. Parent move completed but sidecar was not cascaded.");
            return SidecarOutcome.Failed;
        }

        try
        {
            var destFolder = Path.GetDirectoryName(destSidecarPath);
            if (!string.IsNullOrEmpty(destFolder)
                && !Directory.Exists(destFolder))
            {
                Directory.CreateDirectory(destFolder);
            }

            await FileSystemHelper.MoveWithRetryAsync(() => File.Move(sourceSidecarPath, destSidecarPath));
            return SidecarOutcome.Cascaded;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Failed to cascade sidecar move from '{sourceSidecar}' to '{destSidecar}'.");
            return SidecarOutcome.Failed;
        }
    }

    private SidecarOutcome TryCascadeSidecarCopy(ResourceKey source, ResourceKey destination)
    {
        var sourceSidecar = AppendSidecarSuffix(source);
        var destSidecar = AppendSidecarSuffix(destination);
        if (sourceSidecar is null
            || destSidecar is null)
        {
            return SidecarOutcome.NotPresent;
        }

        var registry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolveSourceResult = registry.ResolveResourcePath(sourceSidecar.Value);
        if (resolveSourceResult.IsFailure)
        {
            return SidecarOutcome.NotPresent;
        }
        var sourceSidecarPath = resolveSourceResult.Value;
        if (!File.Exists(sourceSidecarPath))
        {
            return SidecarOutcome.NotPresent;
        }

        var resolveDestResult = registry.ResolveResourcePath(destSidecar.Value);
        if (resolveDestResult.IsFailure)
        {
            _logger.LogWarning($"Failed to resolve sidecar destination '{destSidecar}' for copy from '{source}'.");
            return SidecarOutcome.Failed;
        }
        var destSidecarPath = resolveDestResult.Value;

        if (File.Exists(destSidecarPath))
        {
            _logger.LogWarning($"Sidecar destination '{destSidecar}' already exists. Parent copy completed but sidecar was not cascaded.");
            return SidecarOutcome.Failed;
        }

        try
        {
            var destFolder = Path.GetDirectoryName(destSidecarPath);
            if (!string.IsNullOrEmpty(destFolder)
                && !Directory.Exists(destFolder))
            {
                Directory.CreateDirectory(destFolder);
            }

            File.Copy(sourceSidecarPath, destSidecarPath);
            return SidecarOutcome.Cascaded;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Failed to cascade sidecar copy from '{sourceSidecar}' to '{destSidecar}'.");
            return SidecarOutcome.Failed;
        }
    }

    private SidecarOutcome TryCascadeSidecarDelete(ResourceKey source)
    {
        var sourceSidecar = AppendSidecarSuffix(source);
        if (sourceSidecar is null)
        {
            return SidecarOutcome.NotPresent;
        }

        var registry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var resolveResult = registry.ResolveResourcePath(sourceSidecar.Value);
        if (resolveResult.IsFailure)
        {
            return SidecarOutcome.NotPresent;
        }
        var sidecarPath = resolveResult.Value;
        if (!File.Exists(sidecarPath))
        {
            return SidecarOutcome.NotPresent;
        }

        try
        {
            File.Delete(sidecarPath);
            return SidecarOutcome.Cascaded;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Failed to cascade sidecar delete for '{sourceSidecar}'.");
            return SidecarOutcome.Failed;
        }
    }

    // Returns the sidecar resource key for the given parent, or null when no
    // valid sidecar key can be derived (root-only key, or the parent itself
    // is already a sidecar key — in which case there is nothing to cascade).
    private ResourceKey? AppendSidecarSuffix(ResourceKey key)
    {
        var sidecarService = _workspaceWrapper.WorkspaceService.SidecarService;
        var result = sidecarService.GetSidecarKey(key);
        if (result.IsSuccess)
        {
            return result.Value;
        }
        return null;
    }

    // Clears the read-only attribute from a file before the FS layer performs
    // a move or delete. User intent to move or delete a file overrides the
    // read-only marker the same way Windows Explorer's "delete" prompt does.
    // Best-effort: any IO failure surfaces when the subsequent move/delete
    // itself fails; we don't pre-flight check.
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
            // Best effort; surface the underlying issue from the caller's operation.
        }
    }

    // Recursive read-only clear for folder delete. Directory.Delete(recursive: true)
    // fails if any contained file is read-only, so traverse first.
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
            // Best effort.
        }
    }

    // Recursive folder copy. Mirrors ResourceUtils.CopyFolder but stays internal
    // to the FS layer so the chokepoint owns the destination structure.
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

    // Runs an IO operation under the chokepoint's bounded-retry policy. A file
    // briefly held open by an external editor, antivirus, or backup product
    // clears within milliseconds, so 3 attempts at 50/100/150ms backoff catches
    // the common cases without imposing meaningful latency on the typical-case
    // success. shouldRetry decides whether a particular IOException is worth
    // retrying; read paths exclude FileNotFoundException and
    // DirectoryNotFoundException because the file is genuinely missing.
    // UnauthorizedAccessException is never retried — for reads and writes it
    // almost always means a permission issue (e.g. an ACL the user can't get
    // past), not a transient lock.
    private async Task<Result<T>> RunWithRetryAsync<T>(
        string operationLabel,
        ResourceKey resource,
        string resourcePath,
        Func<Task<T>> operation,
        Func<IOException, bool>? shouldRetry = null)
        where T : notnull
    {
        IOException? lastException = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                var value = await operation();
                if (attempt > 1)
                {
                    _logger.LogWarning($"{operationLabel} succeeded for '{resourcePath}' on attempt {attempt} of {MaxAttempts} after transient IO failures");
                }
                return Result<T>.Ok(value);
            }
            catch (IOException ex) when (shouldRetry?.Invoke(ex) ?? true)
            {
                lastException = ex;
                if (attempt < MaxAttempts)
                {
                    var delay = BaseRetryDelayMs * attempt;
                    _logger.LogWarning(ex, $"{operationLabel} attempt {attempt} failed for '{resourcePath}', retrying after {delay}ms");
                    await Task.Delay(delay);
                }
            }
            catch (Exception ex)
            {
                return Result<T>.Fail($"Failed to {operationLabel.ToLowerInvariant()} file: '{resource}'")
                    .WithException(ex);
            }
        }

        return Result<T>.Fail($"Failed to {operationLabel.ToLowerInvariant()} file after {MaxAttempts} attempts: '{resource}'")
            .WithException(lastException!);
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
            ProjectConstants.CelbridgeStagingFsFolder);
        try
        {
            Directory.CreateDirectory(stagingFolder);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to create staging folder: '{stagingFolder}'")
                .WithException(ex);
        }

        var runResult = await RunWithRetryAsync<bool>(
            operationLabel: "Write",
            resource: resource,
            resourcePath: resourcePath,
            operation: async () =>
            {
                await WriteAtomicAsync(resourcePath, stagingFolder, bytes);
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
    private static async Task WriteAtomicAsync(string resourcePath, string stagingFolder, byte[] bytes)
    {
        var tempPath = Path.Combine(stagingFolder, Guid.NewGuid().ToString("N") + ".tmp");

        try
        {
            await File.WriteAllBytesAsync(tempPath, bytes);
            await FileSystemHelper.MoveWithRetryAsync(() => File.Move(tempPath, resourcePath, overwrite: true));
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
