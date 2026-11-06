using Celbridge.Logging;
using Celbridge.Resources.Helpers;

namespace Celbridge.Resources.Services;

/// <summary>
/// Represents a single undoable file operation.
/// </summary>
internal abstract class FileOperation
{
    // True if the operation goes on the undo stack.
    public abstract bool IsUndoable { get; }

    public abstract Task<Result> ExecuteAsync();

    // Returns a failure when undo would lose the user's work. The check changes nothing.
    public virtual async Task<Result> CheckUndoAsync()
    {
        await Task.CompletedTask;

        return Result.Ok();
    }

    // Reverses the operation. UndoAsync calls this after CheckUndoAsync passes.
    public abstract Task<Result> ApplyUndoAsync();

    public async Task<Result> UndoAsync()
    {
        var checkResult = await CheckUndoAsync();
        if (checkResult.IsFailure)
        {
            return checkResult;
        }

        return await ApplyUndoAsync();
    }

    public virtual Task<Result> RedoAsync() => ExecuteAsync();

    // Only the project root contains the user's files. The other roots contain Celbridge's own files.
    protected static bool IsUnderProjectRoot(ResourceKey resource)
    {
        return resource.Root == ResourceKey.DefaultRoot;
    }

    // Returns a failure when an added item has changed or can't be read. An absent item passes the check.
    protected static async Task<Result<ItemStampComparison>> CheckAddedItemAsync(
        ResourceKey item,
        ItemStamp? recordedStamp,
        ItemStampReader stampReader)
    {
        var comparison = await stampReader.CompareAsync(item, recordedStamp);
        if (comparison == ItemStampComparison.Changed)
        {
            return Result.Fail($"Undo left '{item}' in place because the item has changed or could not be read.");
        }

        return comparison;
    }

    // Deletes an item that passed the undo check. An absent item leaves nothing to do.
    protected static async Task<Result> RemoveItemAsync(ResourceKey item, IResourceFileSystem resourceFileSystem)
    {
        var infoResult = await resourceFileSystem.GetInfoAsync(item);
        if (infoResult.IsFailure)
        {
            return Result.Fail(infoResult);
        }

        if (infoResult.Value.Kind == StorageItemKind.NotFound)
        {
            return Result.Ok();
        }

        var deleteResult = await resourceFileSystem.DeleteAsync(item);
        if (deleteResult.IsFailure)
        {
            return Result.Fail(deleteResult);
        }

        return Result.Ok();
    }
}

/// <summary>
/// Groups a sequence of operations into one undo unit. Undo runs in reverse so
/// each operation's inverse executes against a filesystem in the same shape it
/// saw on the forward pass.
/// </summary>
internal class FileOperationBatch : FileOperation
{
    public List<FileOperation> Operations { get; } = new();

    // Only undoable operations join a batch.
    public override bool IsUndoable => true;

    public override async Task<Result> ExecuteAsync()
    {
        foreach (var operation in Operations)
        {
            var result = await operation.ExecuteAsync();
            if (result.IsFailure)
            {
                return result;
            }
        }
        return Result.Ok();
    }

    // A refused batch removes nothing, so every check runs before the first operation is undone.
    public override async Task<Result> CheckUndoAsync()
    {
        foreach (var operation in Operations)
        {
            var checkResult = await operation.CheckUndoAsync();
            if (checkResult.IsFailure)
            {
                return checkResult;
            }
        }

        return Result.Ok();
    }

    public override async Task<Result> ApplyUndoAsync()
    {
        for (int i = Operations.Count - 1; i >= 0; i--)
        {
            var result = await Operations[i].ApplyUndoAsync();
            if (result.IsFailure)
            {
                return result;
            }
        }
        return Result.Ok();
    }
}

/// <summary>
/// Undoable create-file or create-folder operation. The folder variant runs
/// through the gateway's idempotent CreateFolderAsync. Undo deletes the folder
/// only when the folder is empty. The file variant writes bytes through the
/// gateway. Undo deletes the file permanently, and refuses when the file has
/// changed since the operation created it.
/// </summary>
internal class CreateOperation : FileOperation
{
    private readonly ResourceKey _resource;
    private readonly bool _isFile;
    private readonly byte[]? _content;
    private readonly IResourceFileSystem _resourceFileSystem;
    private readonly ItemStampReader _stampReader;
    private ItemStamp? _stamp;

    public CreateOperation(
        ResourceKey resource,
        byte[] content,
        IResourceFileSystem resourceFileSystem,
        IResourceWalker resourceWalker)
    {
        _resource = resource;
        _isFile = true;
        _content = content;
        _resourceFileSystem = resourceFileSystem;
        _stampReader = new ItemStampReader(resourceFileSystem, resourceWalker);
    }

    public CreateOperation(
        ResourceKey resource,
        IResourceFileSystem resourceFileSystem,
        IResourceWalker resourceWalker)
    {
        _resource = resource;
        _isFile = false;
        _content = null;
        _resourceFileSystem = resourceFileSystem;
        _stampReader = new ItemStampReader(resourceFileSystem, resourceWalker);
    }

    public override bool IsUndoable => IsUnderProjectRoot(_resource);

    public override async Task<Result> ExecuteAsync()
    {
        if (_isFile)
        {
            var infoResult = await _resourceFileSystem.GetInfoAsync(_resource);
            if (infoResult.IsSuccess
                && infoResult.Value.Kind != StorageItemKind.NotFound)
            {
                return Result.Fail($"Resource already exists: '{_resource}'");
            }

            var writeResult = await _resourceFileSystem.WriteAllBytesAsync(_resource, _content!);
            if (writeResult.IsSuccess
                && IsUndoable)
            {
                _stamp = await _stampReader.ReadAsync(_resource);
            }

            return writeResult;
        }

        return await _resourceFileSystem.CreateFolderAsync(_resource);
    }

    public override async Task<Result> CheckUndoAsync()
    {
        // A created folder has no stamp, because undo removes the folder only when the folder is empty.
        if (!_isFile)
        {
            return Result.Ok();
        }

        return await CheckAddedItemAsync(_resource, _stamp, _stampReader);
    }

    public override async Task<Result> ApplyUndoAsync()
    {
        if (_isFile)
        {
            return await RemoveItemAsync(_resource, _resourceFileSystem);
        }

        var infoResult = await _resourceFileSystem.GetInfoAsync(_resource);
        if (infoResult.IsFailure
            || infoResult.Value.Kind == StorageItemKind.NotFound)
        {
            return Result.Ok();
        }

        // Only remove an empty folder. If the user filled it after the
        // original create, leave the contents alone.
        var enumerateResult = await _resourceFileSystem.EnumerateFolderAsync(_resource);
        if (enumerateResult.IsFailure
            || enumerateResult.Value.Count > 0)
        {
            return Result.Ok();
        }

        var deleteResult = await _resourceFileSystem.DeleteAsync(_resource);
        if (deleteResult.IsFailure)
        {
            return Result.Fail(deleteResult);
        }

        return Result.Ok();
    }
}

/// <summary>
/// Undoable copy of a file or folder through the gateway. The bytes-and-sidecar
/// cascade runs inside the gateway's CopyAsync. Undo refuses after a change to
/// the copy or the source, because redo reads the source again.
/// </summary>
internal class CopyOperation : FileOperation
{
    private readonly ResourceKey _source;
    private readonly ResourceKey _dest;
    private readonly IResourceFileSystem _resourceFileSystem;
    private readonly ItemStampReader _stampReader;
    private ItemStamp? _sourceStamp;
    private ItemStamp? _destStamp;

    public CopyOperation(
        ResourceKey source,
        ResourceKey dest,
        IResourceFileSystem resourceFileSystem,
        IResourceWalker resourceWalker)
    {
        _source = source;
        _dest = dest;
        _resourceFileSystem = resourceFileSystem;
        _stampReader = new ItemStampReader(resourceFileSystem, resourceWalker);
    }

    // A copy changes only its destination.
    public override bool IsUndoable => IsUnderProjectRoot(_dest);

    public override async Task<Result> ExecuteAsync()
    {
        // The source's stamp comes first. A source that changes during the copy then fails the undo check.
        if (IsUndoable)
        {
            _sourceStamp = await _stampReader.ReadAsync(_source);
        }

        var copyResult = await _resourceFileSystem.CopyAsync(_source, _dest);
        if (copyResult.IsFailure)
        {
            return Result.Fail(copyResult);
        }

        if (IsUndoable)
        {
            _destStamp = await _stampReader.ReadAsync(_dest);
        }

        return Result.Ok();
    }

    public override async Task<Result> CheckUndoAsync()
    {
        var itemResult = await CheckAddedItemAsync(_dest, _destStamp, _stampReader);
        if (itemResult.IsFailure
            || itemResult.Value == ItemStampComparison.Absent)
        {
            return itemResult;
        }

        var currentSourceStamp = await _stampReader.ReadAsync(_source);
        if (!ItemStamp.Matches(_sourceStamp, currentSourceStamp))
        {
            return Result.Fail($"Undo left '{_dest}' in place because the source '{_source}' has changed, has gone or could not be read.");
        }

        return Result.Ok();
    }

    public override Task<Result> ApplyUndoAsync() => RemoveItemAsync(_dest, _resourceFileSystem);
}

/// <summary>
/// Undoable move of a file or folder through the gateway. The gateway moves
/// the paired sidecar and announces the source removal. Undo moves the
/// resource back the same way.
/// </summary>
internal class MoveOperation : FileOperation
{
    private readonly ResourceKey _source;
    private readonly ResourceKey _dest;
    private readonly IResourceFileSystem _resourceFileSystem;

    public MoveOperation(
        ResourceKey source,
        ResourceKey dest,
        IResourceFileSystem resourceFileSystem)
    {
        _source = source;
        _dest = dest;
        _resourceFileSystem = resourceFileSystem;
    }

    // The gateway only moves within one root, so the source and destination share a root.
    public override bool IsUndoable => IsUnderProjectRoot(_dest);

    public override async Task<Result> ExecuteAsync()
    {
        var moveResult = await _resourceFileSystem.MoveAsync(_source, _dest);
        if (moveResult.IsFailure)
        {
            return Result.Fail(moveResult);
        }

        return Result.Ok();
    }

    public override async Task<Result> ApplyUndoAsync()
    {
        var moveResult = await _resourceFileSystem.MoveAsync(_dest, _source);
        return moveResult.IsSuccess
            ? Result.Ok()
            : Result.Fail(moveResult);
    }
}

/// <summary>
/// Undoable soft-delete through the trash service. The trash service handles
/// the paired sidecar and read-only attribute clearing as one atomic batch.
/// </summary>
internal class DeleteOperation : FileOperation
{
    private readonly ResourceKey _resource;
    private readonly ITrashService _trashService;
    private TrashEntry? _trashEntry;

    public TrashEntry? TrashEntry => _trashEntry;

    public DeleteOperation(ResourceKey resource, ITrashService trashService)
    {
        _resource = resource;
        _trashService = trashService;
    }

    public override bool IsUndoable => IsUnderProjectRoot(_resource);

    public override async Task<Result> ExecuteAsync()
    {
        var result = await _trashService.MoveToTrashAsync(_resource);
        if (result.IsFailure)
        {
            return Result.Fail(result);
        }

        _trashEntry = result.Value;
        return Result.Ok();
    }

    public override async Task<Result> ApplyUndoAsync()
    {
        if (_trashEntry is null)
        {
            return Result.Fail($"No trash entry to restore for resource: '{_resource}'");
        }

        return await _trashService.RestoreFromTrashAsync(_trashEntry);
    }

    public async Task CleanupAsync()
    {
        if (_trashEntry is null)
        {
            return;
        }

        await _trashService.PurgeAsync(_trashEntry);
    }
}

/// <summary>
/// Undoable import of a file or folder from outside the project. An external
/// import has no paired sidecar in the project, so the sidecar cascade does
/// not apply. The source bytes are read directly, since the source is outside
/// every root. The destination goes through the gateway for containment
/// validation. Undo refuses after a change to the imported item or the source,
/// because redo reads the source again.
/// </summary>
internal class ImportExternalOperation : FileOperation
{
    private readonly string _sourcePath;
    private readonly ResourceKey _dest;
    private readonly bool _isFolder;
    private readonly IResourceFileSystem _resourceFileSystem;
    private readonly ILocalFileSystem _fileSystem;
    private readonly ILogger _logger;
    private readonly ItemStampReader _stampReader;
    private readonly List<FailedResource> _leftOutLinks = new();
    private ItemStamp? _sourceStamp;
    private ItemStamp? _destStamp;

    // Lists each link the folder import left out, keyed by where it would have landed.
    public IReadOnlyList<FailedResource> LeftOutLinks => _leftOutLinks;

    public ImportExternalOperation(
        string sourcePath,
        ResourceKey dest,
        bool isFolder,
        IResourceFileSystem resourceFileSystem,
        IResourceWalker resourceWalker,
        ILocalFileSystem fileSystem,
        ILogger logger)
    {
        _sourcePath = sourcePath;
        _dest = dest;
        _isFolder = isFolder;
        _resourceFileSystem = resourceFileSystem;
        _fileSystem = fileSystem;
        _logger = logger;
        _stampReader = new ItemStampReader(resourceFileSystem, resourceWalker);
    }

    public override bool IsUndoable => IsUnderProjectRoot(_dest);

    public override async Task<Result> ExecuteAsync()
    {
        // The source's stamp comes first. A source that changes during the import then fails the undo check.
        if (IsUndoable)
        {
            _sourceStamp = await ItemStampReader.ReadExternalAsync(_sourcePath, _isFolder, _fileSystem);
        }

        var importResult = await ImportAsync();
        if (importResult.IsSuccess
            && IsUndoable)
        {
            _destStamp = await _stampReader.ReadAsync(_dest);
        }

        return importResult;
    }

    public override async Task<Result> CheckUndoAsync()
    {
        var itemResult = await CheckAddedItemAsync(_dest, _destStamp, _stampReader);
        if (itemResult.IsFailure
            || itemResult.Value == ItemStampComparison.Absent)
        {
            return itemResult;
        }

        var currentSourceStamp = await ItemStampReader.ReadExternalAsync(_sourcePath, _isFolder, _fileSystem);
        if (!ItemStamp.Matches(_sourceStamp, currentSourceStamp))
        {
            return Result.Fail($"Undo left '{_dest}' in place because the source '{_sourcePath}' has changed, has gone or could not be read.");
        }

        return Result.Ok();
    }

    public override Task<Result> ApplyUndoAsync() => RemoveItemAsync(_dest, _resourceFileSystem);

    private async Task<Result> ImportAsync()
    {
        if (_isFolder)
        {
            var sourceFolderInfo = await _fileSystem.GetInfoAsync(_sourcePath);
            if (sourceFolderInfo.IsSuccess
                && sourceFolderInfo.Value.IsLink)
            {
                return RefuseLink(_sourcePath);
            }

            if (sourceFolderInfo.IsFailure
                || sourceFolderInfo.Value.Kind != StorageItemKind.Folder)
            {
                return Result.Fail($"Source folder does not exist: '{_sourcePath}'");
            }

            var infoResult = await _resourceFileSystem.GetInfoAsync(_dest);
            if (infoResult.IsSuccess
                && infoResult.Value.Kind != StorageItemKind.NotFound)
            {
                return Result.Fail($"Destination already exists: '{_dest}'");
            }

            return await ImportFolderAsync(_sourcePath, _dest);
        }

        var sourceFileInfo = await _fileSystem.GetInfoAsync(_sourcePath);
        if (sourceFileInfo.IsSuccess
            && sourceFileInfo.Value.IsLink)
        {
            return RefuseLink(_sourcePath);
        }

        if (sourceFileInfo.IsFailure
            || sourceFileInfo.Value.Kind != StorageItemKind.File)
        {
            return Result.Fail($"Source file does not exist: '{_sourcePath}'");
        }

        var destInfoResult = await _resourceFileSystem.GetInfoAsync(_dest);
        if (destInfoResult.IsSuccess
            && destInfoResult.Value.Kind != StorageItemKind.NotFound)
        {
            return Result.Fail($"Destination already exists: '{_dest}'");
        }

        var readResult = await _fileSystem.ReadAllBytesAsync(_sourcePath);
        if (readResult.IsFailure)
        {
            _logger.LogError($"Failed to read external source file '{_sourcePath}'. {readResult.DiagnosticReport}");
            return Result.Fail($"Failed to import external file from '{_sourcePath}' to '{_dest}'")
                .WithErrors(readResult);
        }
        return await _resourceFileSystem.WriteAllBytesAsync(_dest, readResult.Value);
    }

    private async Task<Result> ImportFolderAsync(string sourceFolderPath, ResourceKey destinationFolder)
    {
        var createResult = await _resourceFileSystem.CreateFolderAsync(destinationFolder);
        if (createResult.IsFailure)
        {
            return createResult;
        }

        var enumerateResult = await _fileSystem.EnumerateAsync(sourceFolderPath, "*", recursive: false);
        if (enumerateResult.IsFailure)
        {
            return Result.Fail(enumerateResult);
        }

        // An import would copy what a link points at, so links are left out and the caller reports them.
        var entries = new List<FileSystemEntry>();
        foreach (var entry in enumerateResult.Value)
        {
            if (entry.IsLink)
            {
                var linkName = Path.GetFileName(entry.FullPath);
                var linkError = new LinkNotFollowedError(entry.FullPath);
                _leftOutLinks.Add(new FailedResource(destinationFolder.Combine(linkName), linkError.Message)
                {
                    Reason = linkError
                });
                continue;
            }

            entries.Add(entry);
        }

        foreach (var entry in entries)
        {
            if (entry.IsFolder)
            {
                continue;
            }
            var fileName = Path.GetFileName(entry.FullPath);
            var destinationFile = destinationFolder.Combine(fileName);
            var readResult = await _fileSystem.ReadAllBytesAsync(entry.FullPath);
            if (readResult.IsFailure)
            {
                return Result.Fail(readResult);
            }
            var writeResult = await _resourceFileSystem.WriteAllBytesAsync(destinationFile, readResult.Value);
            if (writeResult.IsFailure)
            {
                return writeResult;
            }
        }

        foreach (var entry in entries)
        {
            if (!entry.IsFolder)
            {
                continue;
            }
            var folderName = Path.GetFileName(entry.FullPath);
            var destinationSubFolder = destinationFolder.Combine(folderName);
            var recurseResult = await ImportFolderAsync(entry.FullPath, destinationSubFolder);
            if (recurseResult.IsFailure)
            {
                return recurseResult;
            }
        }

        return Result.Ok();
    }

    private static Result RefuseLink(string linkPath)
    {
        var error = new LinkNotFollowedError(linkPath);

        return Result.Fail().WithException(error);
    }
}
