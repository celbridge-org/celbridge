using Celbridge.Logging;
using Celbridge.Resources.Helpers;

namespace Celbridge.Resources.Services;

/// <summary>
/// Represents a single undoable file operation.
/// </summary>
internal abstract class FileOperation
{
    public abstract Task<Result> ExecuteAsync();
    public abstract Task<Result> UndoAsync();
    public virtual Task<Result> RedoAsync() => ExecuteAsync();
}

/// <summary>
/// Groups a sequence of operations into one undo unit. Undo runs in reverse so
/// each operation's inverse executes against a filesystem in the same shape it
/// saw on the forward pass.
/// </summary>
internal class FileOperationBatch : FileOperation
{
    public List<FileOperation> Operations { get; } = new();

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

    public override async Task<Result> UndoAsync()
    {
        for (int i = Operations.Count - 1; i >= 0; i--)
        {
            var result = await Operations[i].UndoAsync();
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
/// through the gateway's idempotent CreateFolderAsync; undo deletes the
/// folder only when it is still empty so user content added after creation is
/// not silently wiped. The file variant writes bytes through the gateway;
/// undo hard-deletes (no trash) since the user is reversing a just-created
/// resource they did not previously want.
/// </summary>
internal class CreateOperation : FileOperation
{
    private readonly ResourceKey _resource;
    private readonly bool _isFile;
    private readonly byte[]? _content;
    private readonly IResourceFileSystem _resourceFileSystem;

    public CreateOperation(ResourceKey resource, byte[] content, IResourceFileSystem resourceFileSystem)
    {
        _resource = resource;
        _isFile = true;
        _content = content;
        _resourceFileSystem = resourceFileSystem;
    }

    public CreateOperation(ResourceKey resource, IResourceFileSystem resourceFileSystem)
    {
        _resource = resource;
        _isFile = false;
        _content = null;
        _resourceFileSystem = resourceFileSystem;
    }

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

            return await _resourceFileSystem.WriteAllBytesAsync(_resource, _content!);
        }

        return await _resourceFileSystem.CreateFolderAsync(_resource);
    }

    public override async Task<Result> UndoAsync()
    {
        var infoResult = await _resourceFileSystem.GetInfoAsync(_resource);
        if (infoResult.IsFailure
            || infoResult.Value.Kind == StorageItemKind.NotFound)
        {
            return Result.Ok();
        }

        if (!_isFile)
        {
            // Only remove an empty folder. If the user filled it after the
            // original create, leave the contents alone.
            var enumerateResult = await _resourceFileSystem.EnumerateFolderAsync(_resource);
            if (enumerateResult.IsFailure
                || enumerateResult.Value.Count > 0)
            {
                return Result.Ok();
            }
        }

        var deleteResult = await _resourceFileSystem.DeleteAsync(_resource);
        return deleteResult.IsSuccess
            ? Result.Ok()
            : Result.Fail(deleteResult);
    }
}

/// <summary>
/// Undoable copy of a file or folder through the gateway. The entity-data
/// cascade runs alongside via EntityFileHelper; the bytes-and-sidecar cascade
/// runs inside the gateway's CopyAsync.
/// </summary>
internal class CopyOperation : FileOperation
{
    private readonly ResourceKey _source;
    private readonly ResourceKey _dest;
    private readonly bool _isFolder;
    private readonly EntityFileHelper _entityHelper;
    private readonly IResourceFileSystem _resourceFileSystem;
    private readonly string _sourcePath;
    private readonly string _destPath;

    public CopyResult? LastCopyResult { get; private set; }

    public CopyOperation(
        ResourceKey source,
        ResourceKey dest,
        bool isFolder,
        string sourcePath,
        string destPath,
        EntityFileHelper entityHelper,
        IResourceFileSystem resourceFileSystem)
    {
        _source = source;
        _dest = dest;
        _isFolder = isFolder;
        _sourcePath = sourcePath;
        _destPath = destPath;
        _entityHelper = entityHelper;
        _resourceFileSystem = resourceFileSystem;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!_isFolder)
        {
            _entityHelper.CopyEntityDataFile(_sourcePath, _destPath);
        }

        var copyResult = await _resourceFileSystem.CopyAsync(_source, _dest);
        if (copyResult.IsFailure)
        {
            return Result.Fail(copyResult);
        }

        if (_isFolder)
        {
            _entityHelper.CopyFolderEntityDataFiles(_sourcePath, _destPath);
        }

        LastCopyResult = copyResult.Value;
        return Result.Ok();
    }

    public override async Task<Result> UndoAsync()
    {
        if (_isFolder)
        {
            _entityHelper.DeleteFolderEntityDataFiles(_destPath);
        }
        else
        {
            _entityHelper.DeleteEntityDataFile(_destPath);
        }

        var deleteResult = await _resourceFileSystem.DeleteAsync(_dest);
        return deleteResult.IsSuccess
            ? Result.Ok()
            : Result.Fail(deleteResult);
    }
}

/// <summary>
/// Undoable move of a file or folder through the gateway. The gateway
/// handles references, the paired sidecar, and the source-removal broadcast;
/// the inverse re-walks references in the opposite direction.
/// </summary>
internal class MoveOperation : FileOperation
{
    private readonly ResourceKey _source;
    private readonly ResourceKey _dest;
    private readonly bool _isFolder;
    private readonly EntityFileHelper _entityHelper;
    private readonly IResourceFileSystem _resourceFileSystem;
    private readonly string _sourcePath;
    private readonly string _destPath;

    public MoveResult? LastMoveResult { get; private set; }

    public MoveOperation(
        ResourceKey source,
        ResourceKey dest,
        bool isFolder,
        string sourcePath,
        string destPath,
        EntityFileHelper entityHelper,
        IResourceFileSystem resourceFileSystem)
    {
        _source = source;
        _dest = dest;
        _isFolder = isFolder;
        _sourcePath = sourcePath;
        _destPath = destPath;
        _entityHelper = entityHelper;
        _resourceFileSystem = resourceFileSystem;
    }

    public override async Task<Result> ExecuteAsync()
    {
        // Entity-data cascade runs while the source still resolves so the
        // helper can compute keys against the original location.
        if (_isFolder)
        {
            _entityHelper.MoveFolderEntityDataFiles(_sourcePath, _destPath);
        }
        else
        {
            _entityHelper.MoveEntityDataFile(_sourcePath, _destPath);
        }

        var moveResult = await _resourceFileSystem.MoveAsync(_source, _dest);
        if (moveResult.IsFailure)
        {
            // Best-effort rollback of the entity-data cascade so the bytes
            // stay paired with the source on failure. Errors here are swallowed
            // because the gateway failure is the load-bearing problem; the
            // entity system is on its way out and the precise post-failure
            // state is not worth a partial-recovery report.
            try
            {
                if (_isFolder)
                {
                    _entityHelper.MoveFolderEntityDataFiles(_destPath, _sourcePath);
                }
                else
                {
                    _entityHelper.MoveEntityDataFile(_destPath, _sourcePath);
                }
            }
            catch
            {
            }

            return Result.Fail(moveResult);
        }

        LastMoveResult = moveResult.Value;
        return Result.Ok();
    }

    public override async Task<Result> UndoAsync()
    {
        if (_isFolder)
        {
            _entityHelper.MoveFolderEntityDataFiles(_destPath, _sourcePath);
        }
        else
        {
            _entityHelper.MoveEntityDataFile(_destPath, _sourcePath);
        }

        var moveResult = await _resourceFileSystem.MoveAsync(_dest, _source);
        return moveResult.IsSuccess
            ? Result.Ok()
            : Result.Fail(moveResult);
    }
}

/// <summary>
/// Undoable soft-delete through the trash service. The trash service handles
/// the paired sidecar, entity-data cascade, and read-only attribute clearing
/// as one atomic batch.
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

    public override async Task<Result> UndoAsync()
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
/// Undoable import of a file or folder from outside the project. External
/// imports carry no inbound references or sidecars, so the cascade does not
/// apply. Source bytes are read directly (the source is outside the registry);
/// the destination flows through the gateway for containment validation.
/// </summary>
internal class ImportExternalOperation : FileOperation
{
    private readonly string _sourcePath;
    private readonly ResourceKey _dest;
    private readonly bool _isFolder;
    private readonly IResourceFileSystem _resourceFileSystem;
    private readonly ILocalFileSystem _fileSystem;
    private readonly ILogger _logger;

    public ImportExternalOperation(
        string sourcePath,
        ResourceKey dest,
        bool isFolder,
        IResourceFileSystem resourceFileSystem,
        ILocalFileSystem fileSystem,
        ILogger logger)
    {
        _sourcePath = sourcePath;
        _dest = dest;
        _isFolder = isFolder;
        _resourceFileSystem = resourceFileSystem;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (_isFolder)
        {
            var sourceFolderInfo = await _fileSystem.GetInfoAsync(_sourcePath);
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

    public override async Task<Result> UndoAsync()
    {
        var infoResult = await _resourceFileSystem.GetInfoAsync(_dest);
        if (infoResult.IsFailure
            || infoResult.Value.Kind == StorageItemKind.NotFound)
        {
            return Result.Ok();
        }

        var deleteResult = await _resourceFileSystem.DeleteAsync(_dest);
        return deleteResult.IsSuccess
            ? Result.Ok()
            : Result.Fail(deleteResult);
    }

    private async Task<Result> ImportFolderAsync(string sourceFolderPath, ResourceKey destinationFolder)
    {
        var createResult = await _resourceFileSystem.CreateFolderAsync(destinationFolder);
        if (createResult.IsFailure)
        {
            return createResult;
        }

        var filesResult = await _fileSystem.EnumerateFilesAsync(sourceFolderPath, "*", recursive: false);
        if (filesResult.IsFailure)
        {
            return Result.Fail(filesResult);
        }
        foreach (var file in filesResult.Value)
        {
            var fileName = Path.GetFileName(file);
            var destinationFile = destinationFolder.Combine(fileName);
            var readResult = await _fileSystem.ReadAllBytesAsync(file);
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

        var foldersResult = await _fileSystem.EnumerateFoldersAsync(sourceFolderPath);
        if (foldersResult.IsFailure)
        {
            return Result.Fail(foldersResult);
        }
        foreach (var subFolder in foldersResult.Value)
        {
            var folderName = Path.GetFileName(subFolder);
            var destinationSubFolder = destinationFolder.Combine(folderName);
            var recurseResult = await ImportFolderAsync(subFolder, destinationSubFolder);
            if (recurseResult.IsFailure)
            {
                return recurseResult;
            }
        }

        return Result.Ok();
    }
}
