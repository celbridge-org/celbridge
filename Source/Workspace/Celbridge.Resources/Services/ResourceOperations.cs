using Celbridge.Entities;
using Celbridge.Resources.Helpers;

namespace Celbridge.Resources.Services;

/// <summary>
/// Represents a single undoable file operation.
/// </summary>
internal abstract class FileOperation
{
    /// <summary>
    /// Executes the operation for the first time or re-executes it (redo).
    /// </summary>
    public abstract Task<Result> ExecuteAsync();

    /// <summary>
    /// Reverses the operation.
    /// </summary>
    public abstract Task<Result> UndoAsync();

    /// <summary>
    /// Re-executes the operation after it was undone.
    /// </summary>
    public virtual Task<Result> RedoAsync() => ExecuteAsync();
}

/// <summary>
/// Represents a group of file operations that should be undone/redone together.
/// </summary>
internal class FileOperationBatch : FileOperation
{
    public List<FileOperation> Operations { get; } = new();

    public override async Task<Result> ExecuteAsync()
    {
        foreach (var op in Operations)
        {
            var result = await op.ExecuteAsync();
            if (result.IsFailure)
            {
                return result;
            }
        }
        return Result.Ok();
    }

    public override async Task<Result> UndoAsync()
    {
        // Undo in reverse order
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
/// Undoable copy file operation. The bytes-and-sidecar cascade runs through
/// IResourceFileSystem.CopyAsync; entity-data cascade rides alongside via
/// EntityFileHelper.
/// </summary>
internal class CopyFileOperation : FileOperation
{
    private readonly string _sourcePath;
    private readonly string _destPath;
    private readonly ResourceKey _sourceKey;
    private readonly ResourceKey _destKey;
    private readonly EntityFileHelper _entityHelper;
    private readonly IResourceFileSystem _fileSystem;

    public CopyResult? LastCopyResult { get; private set; }

    public CopyFileOperation(
        string sourcePath,
        string destPath,
        ResourceKey sourceKey,
        ResourceKey destKey,
        IEntityService? entityService,
        IResourceRegistry? resourceRegistry,
        IResourceFileSystem fileSystem)
    {
        _sourcePath = sourcePath;
        _destPath = destPath;
        _sourceKey = sourceKey;
        _destKey = destKey;
        _entityHelper = new EntityFileHelper(entityService, resourceRegistry);
        _fileSystem = fileSystem;
    }

    public override async Task<Result> ExecuteAsync()
    {
        _entityHelper.CopyEntityDataFile(_sourcePath, _destPath);

        var copyResult = await _fileSystem.CopyAsync(_sourceKey, _destKey);
        if (copyResult.IsFailure)
        {
            return Result.Fail(copyResult);
        }

        LastCopyResult = copyResult.Value;
        return Result.Ok();
    }

    public override async Task<Result> UndoAsync()
    {
        _entityHelper.DeleteEntityDataFile(_destPath);

        var deleteResult = await _fileSystem.DeleteAsync(_destKey);
        if (deleteResult.IsFailure)
        {
            return Result.Fail(deleteResult);
        }

        return Result.Ok();
    }
}

/// <summary>
/// Undoable move file operation. Bytes, reference rewrites, and sidecar cascade
/// run through IResourceFileSystem.MoveAsync; the inverse re-walks the reference
/// graph in the opposite direction so undo restores references too.
/// </summary>
internal class MoveFileOperation : FileOperation
{
    private readonly string _sourcePath;
    private readonly string _destPath;
    private readonly ResourceKey _sourceKey;
    private readonly ResourceKey _destKey;
    private readonly EntityFileHelper _entityHelper;
    private readonly IResourceFileSystem _fileSystem;

    public MoveResult? LastMoveResult { get; private set; }

    public MoveFileOperation(
        string sourcePath,
        string destPath,
        ResourceKey sourceKey,
        ResourceKey destKey,
        IEntityService? entityService,
        IResourceRegistry? resourceRegistry,
        IResourceFileSystem fileSystem)
    {
        _sourcePath = sourcePath;
        _destPath = destPath;
        _sourceKey = sourceKey;
        _destKey = destKey;
        _entityHelper = new EntityFileHelper(entityService, resourceRegistry);
        _fileSystem = fileSystem;
    }

    public override async Task<Result> ExecuteAsync()
    {
        // Entity-data cascade runs before the bytes move so the source path
        // still resolves while EntityFileHelper computes the destination key.
        _entityHelper.MoveEntityDataFile(_sourcePath, _destPath);

        var moveResult = await _fileSystem.MoveAsync(_sourceKey, _destKey);
        if (moveResult.IsFailure)
        {
            return Result.Fail(moveResult);
        }

        LastMoveResult = moveResult.Value;
        return Result.Ok();
    }

    public override async Task<Result> UndoAsync()
    {
        _entityHelper.MoveEntityDataFile(_destPath, _sourcePath);

        var moveResult = await _fileSystem.MoveAsync(_destKey, _sourceKey);
        if (moveResult.IsFailure)
        {
            return Result.Fail(moveResult);
        }

        return Result.Ok();
    }
}

/// <summary>
/// Undoable delete file operation.
/// </summary>
internal class DeleteFileOperation : FileOperation
{
    private readonly string _originalPath;
    private readonly string _trashPath;
    private readonly string? _entityDataOriginalPath;
    private readonly string? _entityDataTrashPath;
    private readonly string? _sidecarOriginalPath;
    private readonly string? _sidecarTrashPath;

    public DeleteFileOperation(
        string originalPath,
        string trashPath,
        string? entityDataOriginalPath,
        string? entityDataTrashPath,
        string? sidecarOriginalPath,
        string? sidecarTrashPath)
    {
        _originalPath = originalPath;
        _trashPath = trashPath;
        _entityDataOriginalPath = entityDataOriginalPath;
        _entityDataTrashPath = entityDataTrashPath;
        _sidecarOriginalPath = sidecarOriginalPath;
        _sidecarTrashPath = sidecarTrashPath;
    }

    public override async Task<Result> ExecuteAsync()
    {
        await Task.CompletedTask;

        try
        {
            if (!File.Exists(_originalPath))
            {
                return Result.Fail($"File does not exist: {_originalPath}");
            }

            // Clear read-only so the soft-delete (a File.Move into the trash
            // folder) is not blocked by an attribute the user has explicitly
            // chosen to override by invoking delete. The cleared state persists
            // through undo — restoring a previously-read-only file produces a
            // writable copy. A user who needs the read-only attribute back can
            // re-apply it via the OS file properties dialog.
            ClearReadOnlyIfSet(_originalPath);

            await FileSystemHelper.MoveFileWithDirectoryCreationAsync(_originalPath, _trashPath);

            // Also move entity data file to trash if it exists
            if (!string.IsNullOrEmpty(_entityDataOriginalPath) &&
                !string.IsNullOrEmpty(_entityDataTrashPath) &&
                File.Exists(_entityDataOriginalPath))
            {
                ClearReadOnlyIfSet(_entityDataOriginalPath);
                await FileSystemHelper.MoveFileWithDirectoryCreationAsync(_entityDataOriginalPath, _entityDataTrashPath);
            }

            // Also move the paired sidecar to trash if it exists
            if (!string.IsNullOrEmpty(_sidecarOriginalPath) &&
                !string.IsNullOrEmpty(_sidecarTrashPath) &&
                File.Exists(_sidecarOriginalPath))
            {
                ClearReadOnlyIfSet(_sidecarOriginalPath);
                await FileSystemHelper.MoveFileWithDirectoryCreationAsync(_sidecarOriginalPath, _sidecarTrashPath);
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to delete file: {_originalPath}")
                .WithException(ex);
        }
    }

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
            // Best effort; surface from the subsequent move/delete failure.
        }
    }

    public override async Task<Result> UndoAsync()
    {
        await Task.CompletedTask;

        try
        {
            if (!File.Exists(_trashPath))
            {
                return Result.Fail($"Trash file does not exist: {_trashPath}");
            }

            await FileSystemHelper.MoveFileWithDirectoryCreationAsync(_trashPath, _originalPath);

            // Also restore entity data file if it was trashed
            if (!string.IsNullOrEmpty(_entityDataOriginalPath) &&
                !string.IsNullOrEmpty(_entityDataTrashPath) &&
                File.Exists(_entityDataTrashPath))
            {
                await FileSystemHelper.MoveFileWithDirectoryCreationAsync(_entityDataTrashPath, _entityDataOriginalPath);
            }

            // Also restore the paired sidecar if it was trashed
            if (!string.IsNullOrEmpty(_sidecarOriginalPath) &&
                !string.IsNullOrEmpty(_sidecarTrashPath) &&
                File.Exists(_sidecarTrashPath))
            {
                await FileSystemHelper.MoveFileWithDirectoryCreationAsync(_sidecarTrashPath, _sidecarOriginalPath);
            }

            FileSystemHelper.CleanupEmptyParentDirectories(_trashPath);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to restore deleted file: {_originalPath}")
                .WithException(ex);
        }
    }

    /// <summary>
    /// Clean up the trash file when this operation is discarded from the undo stack.
    /// </summary>
    public void CleanupTrashFile()
    {
        try
        {
            FileSystemHelper.DeleteFileIfExists(_trashPath);

            if (!string.IsNullOrEmpty(_entityDataTrashPath))
            {
                FileSystemHelper.DeleteFileIfExists(_entityDataTrashPath);
            }

            if (!string.IsNullOrEmpty(_sidecarTrashPath))
            {
                FileSystemHelper.DeleteFileIfExists(_sidecarTrashPath);
            }

            FileSystemHelper.CleanupEmptyParentDirectories(_trashPath);
        }
        catch
        {
            // Best effort cleanup - ignore errors
        }
    }
}

/// <summary>
/// Undoable copy folder operation. Bytes-and-sidecar cascade runs through
/// IResourceFileSystem.CopyAsync; entity-data cascade rides alongside via
/// EntityFileHelper.
/// </summary>
internal class CopyFolderOperation : FileOperation
{
    private readonly string _sourcePath;
    private readonly string _destPath;
    private readonly ResourceKey _sourceKey;
    private readonly ResourceKey _destKey;
    private readonly EntityFileHelper _entityHelper;
    private readonly IResourceFileSystem _fileSystem;

    public CopyResult? LastCopyResult { get; private set; }

    public CopyFolderOperation(
        string sourcePath,
        string destPath,
        ResourceKey sourceKey,
        ResourceKey destKey,
        IEntityService? entityService,
        IResourceRegistry? resourceRegistry,
        IResourceFileSystem fileSystem)
    {
        _sourcePath = sourcePath;
        _destPath = destPath;
        _sourceKey = sourceKey;
        _destKey = destKey;
        _entityHelper = new EntityFileHelper(entityService, resourceRegistry);
        _fileSystem = fileSystem;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var copyResult = await _fileSystem.CopyAsync(_sourceKey, _destKey);
        if (copyResult.IsFailure)
        {
            return Result.Fail(copyResult);
        }

        _entityHelper.CopyFolderEntityDataFiles(_sourcePath, _destPath);
        LastCopyResult = copyResult.Value;

        return Result.Ok();
    }

    public override async Task<Result> UndoAsync()
    {
        _entityHelper.DeleteFolderEntityDataFiles(_destPath);

        var deleteResult = await _fileSystem.DeleteAsync(_destKey);
        if (deleteResult.IsFailure)
        {
            return Result.Fail(deleteResult);
        }

        return Result.Ok();
    }
}

/// <summary>
/// Undoable move folder operation. Bytes, reference rewrites, and sidecar
/// cascade run through IResourceFileSystem.MoveAsync; the inverse re-walks the
/// reference graph in the opposite direction.
/// </summary>
internal class MoveFolderOperation : FileOperation
{
    private readonly string _sourcePath;
    private readonly string _destPath;
    private readonly ResourceKey _sourceKey;
    private readonly ResourceKey _destKey;
    private readonly EntityFileHelper _entityHelper;
    private readonly IResourceFileSystem _fileSystem;

    public MoveResult? LastMoveResult { get; private set; }

    public MoveFolderOperation(
        string sourcePath,
        string destPath,
        ResourceKey sourceKey,
        ResourceKey destKey,
        IEntityService? entityService,
        IResourceRegistry? resourceRegistry,
        IResourceFileSystem fileSystem)
    {
        _sourcePath = sourcePath;
        _destPath = destPath;
        _sourceKey = sourceKey;
        _destKey = destKey;
        _entityHelper = new EntityFileHelper(entityService, resourceRegistry);
        _fileSystem = fileSystem;
    }

    public override async Task<Result> ExecuteAsync()
    {
        // Move entity data files first (while source folder still exists for enumeration).
        _entityHelper.MoveFolderEntityDataFiles(_sourcePath, _destPath);

        var moveResult = await _fileSystem.MoveAsync(_sourceKey, _destKey);
        if (moveResult.IsFailure)
        {
            return Result.Fail(moveResult);
        }

        LastMoveResult = moveResult.Value;
        return Result.Ok();
    }

    public override async Task<Result> UndoAsync()
    {
        // Move entity data files back first (while dest folder still exists for enumeration).
        _entityHelper.MoveFolderEntityDataFiles(_destPath, _sourcePath);

        var moveResult = await _fileSystem.MoveAsync(_destKey, _sourceKey);
        if (moveResult.IsFailure)
        {
            return Result.Fail(moveResult);
        }

        return Result.Ok();
    }
}

/// <summary>
/// Undoable delete folder operation.
/// </summary>
internal class DeleteFolderOperation : FileOperation
{
    private readonly string _originalPath;
    private readonly string _trashPath;
    private readonly bool _wasEmpty;
    private readonly List<(string OriginalPath, string TrashPath)> _entityDataFiles;

    public DeleteFolderOperation(
        string originalPath, 
        string trashPath, 
        bool wasEmpty,
        List<(string OriginalPath, string TrashPath)> entityDataFiles)
    {
        _originalPath = originalPath;
        _trashPath = trashPath;
        _wasEmpty = wasEmpty;
        _entityDataFiles = entityDataFiles;
    }

    public override async Task<Result> ExecuteAsync()
    {
        await Task.CompletedTask;

        try
        {
            if (!Directory.Exists(_originalPath))
            {
                return Result.Fail($"Folder does not exist: {_originalPath}");
            }

            // Clear read-only on every contained file so the folder move into
            // trash (or the empty-folder Directory.Delete) is not blocked by an
            // attribute the user has explicitly chosen to override by invoking
            // delete on the parent folder.
            ClearReadOnlyRecursive(_originalPath);

            if (FileSystemHelper.IsDirectoryEmpty(_originalPath))
            {
                Directory.Delete(_originalPath);
            }
            else
            {
                // Move entity data files to trash first
                await MoveEntityDataFilesToTrashAsync();

                // Non-empty folder - move to trash
                await FileSystemHelper.MoveDirectoryWithParentCreationAsync(_originalPath, _trashPath);
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to delete folder: {_originalPath}")
                .WithException(ex);
        }
    }

    private static void ClearReadOnlyRecursive(string folder)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.Exists
                        && info.IsReadOnly)
                    {
                        info.IsReadOnly = false;
                    }
                }
                catch
                {
                    // Best effort per file; surface aggregate via the delete failure.
                }
            }
        }
        catch
        {
            // Best effort traversal.
        }
    }

    public override async Task<Result> UndoAsync()
    {
        await Task.CompletedTask;

        try
        {
            if (_wasEmpty)
            {
                Directory.CreateDirectory(_originalPath);
            }
            else
            {
                if (!Directory.Exists(_trashPath))
                {
                    return Result.Fail($"Trash folder does not exist: {_trashPath}");
                }

                await FileSystemHelper.MoveDirectoryWithParentCreationAsync(_trashPath, _originalPath);

                // Restore entity data files from trash
                await RestoreEntityDataFilesAsync();

                FileSystemHelper.CleanupEmptyParentDirectories(_trashPath);
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to restore deleted folder: {_originalPath}")
                .WithException(ex);
        }
    }

    /// <summary>
    /// Clean up the trash folder when this operation is discarded from the undo stack.
    /// </summary>
    public void CleanupTrashFolder()
    {
        try
        {
            if (!_wasEmpty && Directory.Exists(_trashPath))
            {
                Directory.Delete(_trashPath, recursive: true);
                FileSystemHelper.CleanupEmptyParentDirectories(_trashPath);
            }

            // Also clean up entity data files in trash
            foreach (var (_, trashPath) in _entityDataFiles)
            {
                FileSystemHelper.DeleteFileIfExists(trashPath);
                FileSystemHelper.CleanupEmptyParentDirectories(trashPath);
            }
        }
        catch
        {
            // Best effort cleanup - ignore errors
        }
    }

    private async Task MoveEntityDataFilesToTrashAsync()
    {
        foreach (var (originalPath, trashPath) in _entityDataFiles)
        {
            if (File.Exists(originalPath))
            {
                await FileSystemHelper.MoveFileWithDirectoryCreationAsync(originalPath, trashPath);
                FileSystemHelper.CleanupEmptyParentDirectories(originalPath);
            }
        }
    }

    private async Task RestoreEntityDataFilesAsync()
    {
        foreach (var (originalPath, trashPath) in _entityDataFiles)
        {
            if (File.Exists(trashPath))
            {
                await FileSystemHelper.MoveFileWithDirectoryCreationAsync(trashPath, originalPath);
                FileSystemHelper.CleanupEmptyParentDirectories(trashPath);
            }
        }
    }
}

/// <summary>
/// Undoable copy of bytes from outside the project folder (file). External
/// imports carry no inbound references or sidecars, so the cascade does not
/// apply; this operation does a direct File.Copy and tracks undo as a delete.
/// </summary>
internal class CopyExternalFileOperation : FileOperation
{
    private readonly string _sourcePath;
    private readonly string _destPath;

    public CopyExternalFileOperation(string sourcePath, string destPath)
    {
        _sourcePath = sourcePath;
        _destPath = destPath;
    }

    public override async Task<Result> ExecuteAsync()
    {
        await Task.CompletedTask;

        try
        {
            if (!File.Exists(_sourcePath))
            {
                return Result.Fail($"Source file does not exist: {_sourcePath}");
            }
            if (File.Exists(_destPath))
            {
                return Result.Fail($"Destination file already exists: {_destPath}");
            }

            var destFolder = Path.GetDirectoryName(_destPath);
            if (!string.IsNullOrEmpty(destFolder)
                && !Directory.Exists(destFolder))
            {
                Directory.CreateDirectory(destFolder);
            }

            File.Copy(_sourcePath, _destPath);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to copy external file: {_sourcePath} to {_destPath}")
                .WithException(ex);
        }
    }

    public override async Task<Result> UndoAsync()
    {
        await Task.CompletedTask;

        try
        {
            if (File.Exists(_destPath))
            {
                File.Delete(_destPath);
            }
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to undo external file copy: {_destPath}")
                .WithException(ex);
        }
    }
}

/// <summary>
/// Undoable copy of bytes from outside the project folder (folder). Mirrors
/// CopyExternalFileOperation; no cascade applies.
/// </summary>
internal class CopyExternalFolderOperation : FileOperation
{
    private readonly string _sourcePath;
    private readonly string _destPath;

    public CopyExternalFolderOperation(string sourcePath, string destPath)
    {
        _sourcePath = sourcePath;
        _destPath = destPath;
    }

    public override async Task<Result> ExecuteAsync()
    {
        await Task.CompletedTask;

        try
        {
            if (!Directory.Exists(_sourcePath))
            {
                return Result.Fail($"Source folder does not exist: {_sourcePath}");
            }
            if (Directory.Exists(_destPath))
            {
                return Result.Fail($"Destination folder already exists: {_destPath}");
            }

            ResourceUtils.CopyFolder(_sourcePath, _destPath);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to copy external folder: {_sourcePath} to {_destPath}")
                .WithException(ex);
        }
    }

    public override async Task<Result> UndoAsync()
    {
        await Task.CompletedTask;

        try
        {
            if (Directory.Exists(_destPath))
            {
                Directory.Delete(_destPath, recursive: true);
            }
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to undo external folder copy: {_destPath}")
                .WithException(ex);
        }
    }
}

/// <summary>
/// Undoable create file operation.
/// Undo deletes the created file. Redo recreates it.
/// </summary>
internal class CreateFileOperation : FileOperation
{
    private readonly string _filePath;
    private readonly byte[] _content;

    public CreateFileOperation(string filePath, byte[] content)
    {
        _filePath = filePath;
        _content = content;
    }

    public override async Task<Result> ExecuteAsync()
    {
        await Task.CompletedTask;

        try
        {
            if (File.Exists(_filePath))
            {
                return Result.Fail($"File already exists: {_filePath}");
            }

            var parentFolder = Path.GetDirectoryName(_filePath);
            if (!Directory.Exists(parentFolder))
            {
                return Result.Fail($"Parent folder does not exist: {parentFolder}");
            }

            File.WriteAllBytes(_filePath, _content);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to create file: {_filePath}")
                .WithException(ex);
        }
    }

    public override async Task<Result> UndoAsync()
    {
        await Task.CompletedTask;

        try
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to undo create file: {_filePath}")
                .WithException(ex);
        }
    }
}

/// <summary>
/// Undoable create folder operation.
/// Undo deletes the created folder. Redo recreates it.
/// </summary>
internal class CreateFolderOperation : FileOperation
{
    private readonly string _folderPath;

    public CreateFolderOperation(string folderPath)
    {
        _folderPath = folderPath;
    }

    public override async Task<Result> ExecuteAsync()
    {
        await Task.CompletedTask;

        try
        {
            if (Directory.Exists(_folderPath))
            {
                return Result.Fail($"Folder already exists: {_folderPath}");
            }

            // Directory.CreateDirectory handles intermediate parent folders automatically
            Directory.CreateDirectory(_folderPath);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to create folder: {_folderPath}")
                .WithException(ex);
        }
    }

    public override async Task<Result> UndoAsync()
    {
        await Task.CompletedTask;

        try
        {
            if (Directory.Exists(_folderPath))
            {
                // Only delete if empty - if user added content, don't delete it
                var files = Directory.GetFiles(_folderPath);
                var dirs = Directory.GetDirectories(_folderPath);
                if (files.Length == 0 && dirs.Length == 0)
                {
                    Directory.Delete(_folderPath);
                }
            }
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to undo create folder: {_folderPath}")
                .WithException(ex);
        }
    }
}
