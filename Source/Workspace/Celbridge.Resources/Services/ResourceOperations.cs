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
/// through the chokepoint's idempotent CreateFolderAsync; undo deletes the
/// folder only when it is still empty so user content added after creation is
/// not silently wiped. The file variant writes bytes through the chokepoint;
/// undo hard-deletes (no trash) since the user is reversing a just-created
/// resource they did not previously want.
/// </summary>
internal class CreateOperation : FileOperation
{
    private readonly ResourceKey _resource;
    private readonly bool _isFile;
    private readonly byte[]? _content;
    private readonly IFileStorage _fileStorage;

    public CreateOperation(ResourceKey resource, byte[] content, IFileStorage fileStorage)
    {
        _resource = resource;
        _isFile = true;
        _content = content;
        _fileStorage = fileStorage;
    }

    public CreateOperation(ResourceKey resource, IFileStorage fileStorage)
    {
        _resource = resource;
        _isFile = false;
        _content = null;
        _fileStorage = fileStorage;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (_isFile)
        {
            var infoResult = await _fileStorage.GetInfoAsync(_resource);
            if (infoResult.IsSuccess
                && infoResult.Value.Kind != StorageItemKind.NotFound)
            {
                return Result.Fail($"Resource already exists: '{_resource}'");
            }

            return await _fileStorage.WriteAllBytesAsync(_resource, _content!);
        }

        return await _fileStorage.CreateFolderAsync(_resource);
    }

    public override async Task<Result> UndoAsync()
    {
        var infoResult = await _fileStorage.GetInfoAsync(_resource);
        if (infoResult.IsFailure
            || infoResult.Value.Kind == StorageItemKind.NotFound)
        {
            return Result.Ok();
        }

        if (!_isFile)
        {
            // Only remove an empty folder. If the user filled it after the
            // original create, leave the contents alone.
            var enumerateResult = await _fileStorage.EnumerateFolderAsync(_resource);
            if (enumerateResult.IsFailure
                || enumerateResult.Value.Count > 0)
            {
                return Result.Ok();
            }
        }

        var deleteResult = await _fileStorage.DeleteAsync(_resource);
        return deleteResult.IsSuccess
            ? Result.Ok()
            : Result.Fail(deleteResult);
    }
}

/// <summary>
/// Undoable copy of a file or folder through the chokepoint. The entity-data
/// cascade runs alongside via EntityFileHelper; the bytes-and-sidecar cascade
/// runs inside the chokepoint's CopyAsync.
/// </summary>
internal class CopyOperation : FileOperation
{
    private readonly ResourceKey _source;
    private readonly ResourceKey _destination;
    private readonly bool _isFolder;
    private readonly EntityFileHelper _entityHelper;
    private readonly IFileStorage _fileStorage;
    private readonly string _sourcePath;
    private readonly string _destinationPath;

    public CopyResult? LastCopyResult { get; private set; }

    public CopyOperation(
        ResourceKey source,
        ResourceKey destination,
        bool isFolder,
        string sourcePath,
        string destinationPath,
        EntityFileHelper entityHelper,
        IFileStorage fileStorage)
    {
        _source = source;
        _destination = destination;
        _isFolder = isFolder;
        _sourcePath = sourcePath;
        _destinationPath = destinationPath;
        _entityHelper = entityHelper;
        _fileStorage = fileStorage;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!_isFolder)
        {
            _entityHelper.CopyEntityDataFile(_sourcePath, _destinationPath);
        }

        var copyResult = await _fileStorage.CopyAsync(_source, _destination);
        if (copyResult.IsFailure)
        {
            return Result.Fail(copyResult);
        }

        if (_isFolder)
        {
            _entityHelper.CopyFolderEntityDataFiles(_sourcePath, _destinationPath);
        }

        LastCopyResult = copyResult.Value;
        return Result.Ok();
    }

    public override async Task<Result> UndoAsync()
    {
        if (_isFolder)
        {
            _entityHelper.DeleteFolderEntityDataFiles(_destinationPath);
        }
        else
        {
            _entityHelper.DeleteEntityDataFile(_destinationPath);
        }

        var deleteResult = await _fileStorage.DeleteAsync(_destination);
        return deleteResult.IsSuccess
            ? Result.Ok()
            : Result.Fail(deleteResult);
    }
}

/// <summary>
/// Undoable move of a file or folder through the chokepoint. The chokepoint
/// handles references, the paired sidecar, and the source-removal broadcast;
/// the inverse re-walks references in the opposite direction.
/// </summary>
internal class MoveOperation : FileOperation
{
    private readonly ResourceKey _source;
    private readonly ResourceKey _destination;
    private readonly bool _isFolder;
    private readonly EntityFileHelper _entityHelper;
    private readonly IFileStorage _fileStorage;
    private readonly string _sourcePath;
    private readonly string _destinationPath;

    public MoveResult? LastMoveResult { get; private set; }

    public MoveOperation(
        ResourceKey source,
        ResourceKey destination,
        bool isFolder,
        string sourcePath,
        string destinationPath,
        EntityFileHelper entityHelper,
        IFileStorage fileStorage)
    {
        _source = source;
        _destination = destination;
        _isFolder = isFolder;
        _sourcePath = sourcePath;
        _destinationPath = destinationPath;
        _entityHelper = entityHelper;
        _fileStorage = fileStorage;
    }

    public override async Task<Result> ExecuteAsync()
    {
        // Entity-data cascade runs while the source still resolves so the
        // helper can compute keys against the original location.
        if (_isFolder)
        {
            _entityHelper.MoveFolderEntityDataFiles(_sourcePath, _destinationPath);
        }
        else
        {
            _entityHelper.MoveEntityDataFile(_sourcePath, _destinationPath);
        }

        var moveResult = await _fileStorage.MoveAsync(_source, _destination);
        if (moveResult.IsFailure)
        {
            return Result.Fail(moveResult);
        }

        LastMoveResult = moveResult.Value;
        return Result.Ok();
    }

    public override async Task<Result> UndoAsync()
    {
        if (_isFolder)
        {
            _entityHelper.MoveFolderEntityDataFiles(_destinationPath, _sourcePath);
        }
        else
        {
            _entityHelper.MoveEntityDataFile(_destinationPath, _sourcePath);
        }

        var moveResult = await _fileStorage.MoveAsync(_destination, _source);
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
/// the destination flows through the chokepoint for containment validation.
/// </summary>
internal class ImportExternalOperation : FileOperation
{
    private readonly string _sourcePath;
    private readonly ResourceKey _destination;
    private readonly bool _isFolder;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger _logger;

    public ImportExternalOperation(
        string sourcePath,
        ResourceKey destination,
        bool isFolder,
        IFileStorage fileStorage,
        ILogger logger)
    {
        _sourcePath = sourcePath;
        _destination = destination;
        _isFolder = isFolder;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (_isFolder)
        {
            if (!Directory.Exists(_sourcePath))
            {
                return Result.Fail($"Source folder does not exist: '{_sourcePath}'");
            }

            var infoResult = await _fileStorage.GetInfoAsync(_destination);
            if (infoResult.IsSuccess
                && infoResult.Value.Kind != StorageItemKind.NotFound)
            {
                return Result.Fail($"Destination already exists: '{_destination}'");
            }

            return await ImportFolderAsync(_sourcePath, _destination);
        }

        if (!File.Exists(_sourcePath))
        {
            return Result.Fail($"Source file does not exist: '{_sourcePath}'");
        }

        var destInfoResult = await _fileStorage.GetInfoAsync(_destination);
        if (destInfoResult.IsSuccess
            && destInfoResult.Value.Kind != StorageItemKind.NotFound)
        {
            return Result.Fail($"Destination already exists: '{_destination}'");
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(_sourcePath);
            return await _fileStorage.WriteAllBytesAsync(_destination, bytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import external file from '{SourcePath}' to '{Destination}'", _sourcePath, _destination);
            return Result.Fail($"Failed to import external file from '{_sourcePath}' to '{_destination}'")
                .WithException(ex);
        }
    }

    public override async Task<Result> UndoAsync()
    {
        var infoResult = await _fileStorage.GetInfoAsync(_destination);
        if (infoResult.IsFailure
            || infoResult.Value.Kind == StorageItemKind.NotFound)
        {
            return Result.Ok();
        }

        var deleteResult = await _fileStorage.DeleteAsync(_destination);
        return deleteResult.IsSuccess
            ? Result.Ok()
            : Result.Fail(deleteResult);
    }

    private async Task<Result> ImportFolderAsync(string sourceFolderPath, ResourceKey destinationFolder)
    {
        var createResult = await _fileStorage.CreateFolderAsync(destinationFolder);
        if (createResult.IsFailure)
        {
            return createResult;
        }

        try
        {
            foreach (var file in Directory.GetFiles(sourceFolderPath))
            {
                var fileName = Path.GetFileName(file);
                var destinationFile = destinationFolder.Combine(fileName);
                var bytes = await File.ReadAllBytesAsync(file);
                var writeResult = await _fileStorage.WriteAllBytesAsync(destinationFile, bytes);
                if (writeResult.IsFailure)
                {
                    return writeResult;
                }
            }

            foreach (var subFolder in Directory.GetDirectories(sourceFolderPath))
            {
                var folderName = Path.GetFileName(subFolder);
                var destinationSubFolder = destinationFolder.Combine(folderName);
                var recurseResult = await ImportFolderAsync(subFolder, destinationSubFolder);
                if (recurseResult.IsFailure)
                {
                    return recurseResult;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import external folder from '{SourceFolderPath}' to '{DestinationFolder}'", sourceFolderPath, destinationFolder);
            return Result.Fail($"Failed to import external folder from '{sourceFolderPath}' to '{destinationFolder}'")
                .WithException(ex);
        }

        return Result.Ok();
    }
}
