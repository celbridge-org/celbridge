using Celbridge.Entities;

namespace Celbridge.Explorer.Services;

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
/// Undoable copy file operation.
/// Undo deletes the copied file. Redo copies it again.
/// </summary>
internal class CopyFileOperation : FileOperation
{
    private readonly string _sourcePath;
    private readonly string _destPath;
    private readonly EntityFileHelper _entityHelper;

    public CopyFileOperation(string sourcePath, string destPath, IEntityService? entityService, IResourceRegistry? resourceRegistry)
    {
        _sourcePath = sourcePath;
        _destPath = destPath;
        _entityHelper = new EntityFileHelper(entityService, resourceRegistry);
    }

    public override Task<Result> ExecuteAsync()
    {
        try
        {
            if (!File.Exists(_sourcePath))
            {
                return Task.FromResult((Result)Result.Fail($"Source file does not exist: {_sourcePath}"));
            }

            if (File.Exists(_destPath))
            {
                return Task.FromResult((Result)Result.Fail($"Destination file already exists: {_destPath}"));
            }

            var destFolder = Path.GetDirectoryName(_destPath);
            if (!Directory.Exists(destFolder))
            {
                return Task.FromResult((Result)Result.Fail($"Destination folder does not exist: {destFolder}"));
            }

            _entityHelper.CopyEntityDataFile(_sourcePath, _destPath);
            File.Copy(_sourcePath, _destPath);

            return Task.FromResult(Result.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult((Result)Result.Fail($"Failed to copy file: {_sourcePath} to {_destPath}")
                .WithException(ex));
        }
    }

    public override Task<Result> UndoAsync()
    {
        try
        {
            _entityHelper.DeleteEntityDataFile(_destPath);

            if (File.Exists(_destPath))
            {
                File.Delete(_destPath);
            }
            return Task.FromResult(Result.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult((Result)Result.Fail($"Failed to undo copy file: {_destPath}")
                .WithException(ex));
        }
    }
}

/// <summary>
/// Undoable move file operation.
/// Undo moves the file back. Redo moves it again.
/// </summary>
internal class MoveFileOperation : FileOperation
{
    private readonly string _sourcePath;
    private readonly string _destPath;
    private readonly EntityFileHelper _entityHelper;

    public MoveFileOperation(string sourcePath, string destPath, IEntityService? entityService, IResourceRegistry? resourceRegistry)
    {
        _sourcePath = sourcePath;
        _destPath = destPath;
        _entityHelper = new EntityFileHelper(entityService, resourceRegistry);
    }

    public override Task<Result> ExecuteAsync()
    {
        try
        {
            if (!File.Exists(_sourcePath))
            {
                return Task.FromResult((Result)Result.Fail($"Source file does not exist: {_sourcePath}"));
            }

            // Allow move to same location with different case (case-only rename)
            bool isSameFile = string.Equals(_sourcePath, _destPath, StringComparison.OrdinalIgnoreCase);
            if (File.Exists(_destPath) && !isSameFile)
            {
                return Task.FromResult((Result)Result.Fail($"Destination file already exists: {_destPath}"));
            }

            var destFolder = Path.GetDirectoryName(_destPath);
            if (!Directory.Exists(destFolder))
            {
                return Task.FromResult((Result)Result.Fail($"Destination folder does not exist: {destFolder}"));
            }

            _entityHelper.MoveEntityDataFile(_sourcePath, _destPath);
            File.Move(_sourcePath, _destPath);

            return Task.FromResult(Result.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult((Result)Result.Fail($"Failed to move file: {_sourcePath} to {_destPath}")
                .WithException(ex));
        }
    }

    public override Task<Result> UndoAsync()
    {
        try
        {
            if (!File.Exists(_destPath))
            {
                return Task.FromResult((Result)Result.Fail($"File no longer exists at destination: {_destPath}"));
            }

            _entityHelper.MoveEntityDataFile(_destPath, _sourcePath);
            File.Move(_destPath, _sourcePath);

            return Task.FromResult(Result.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult((Result)Result.Fail($"Failed to undo move file: {_destPath} back to {_sourcePath}")
                .WithException(ex));
        }
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

    public DeleteFileOperation(string originalPath, string trashPath, string? entityDataOriginalPath, string? entityDataTrashPath)
    {
        _originalPath = originalPath;
        _trashPath = trashPath;
        _entityDataOriginalPath = entityDataOriginalPath;
        _entityDataTrashPath = entityDataTrashPath;
    }

    public override Task<Result> ExecuteAsync()
    {
        try
        {
            if (!File.Exists(_originalPath))
            {
                return Task.FromResult((Result)Result.Fail($"File does not exist: {_originalPath}"));
            }

            FileSystemHelper.MoveFileWithDirectoryCreation(_originalPath, _trashPath);

            // Also move entity data file to trash if it exists
            if (!string.IsNullOrEmpty(_entityDataOriginalPath) && 
                !string.IsNullOrEmpty(_entityDataTrashPath) && 
                File.Exists(_entityDataOriginalPath))
            {
                FileSystemHelper.MoveFileWithDirectoryCreation(_entityDataOriginalPath, _entityDataTrashPath);
            }

            return Task.FromResult(Result.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult((Result)Result.Fail($"Failed to delete file: {_originalPath}")
                .WithException(ex));
        }
    }

    public override Task<Result> UndoAsync()
    {
        try
        {
            if (!File.Exists(_trashPath))
            {
                return Task.FromResult((Result)Result.Fail($"Trash file does not exist: {_trashPath}"));
            }

            FileSystemHelper.MoveFileWithDirectoryCreation(_trashPath, _originalPath);

            // Also restore entity data file if it was trashed
            if (!string.IsNullOrEmpty(_entityDataOriginalPath) && 
                !string.IsNullOrEmpty(_entityDataTrashPath) && 
                File.Exists(_entityDataTrashPath))
            {
                FileSystemHelper.MoveFileWithDirectoryCreation(_entityDataTrashPath, _entityDataOriginalPath);
            }

            FileSystemHelper.CleanupEmptyParentDirectories(_trashPath);

            return Task.FromResult(Result.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult((Result)Result.Fail($"Failed to restore deleted file: {_originalPath}")
                .WithException(ex));
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

            FileSystemHelper.CleanupEmptyParentDirectories(_trashPath);
        }
        catch
        {
            // Best effort cleanup - ignore errors
        }
    }
}

/// <summary>
/// Undoable copy folder operation.
/// </summary>
internal class CopyFolderOperation : FileOperation
{
    private readonly string _sourcePath;
    private readonly string _destPath;
    private readonly EntityFileHelper _entityHelper;

    public CopyFolderOperation(string sourcePath, string destPath, IEntityService? entityService, IResourceRegistry? resourceRegistry)
    {
        _sourcePath = sourcePath;
        _destPath = destPath;
        _entityHelper = new EntityFileHelper(entityService, resourceRegistry);
    }

    public override Task<Result> ExecuteAsync()
    {
        try
        {
            if (!Directory.Exists(_sourcePath))
            {
                return Task.FromResult((Result)Result.Fail($"Source folder does not exist: {_sourcePath}"));
            }

            if (Directory.Exists(_destPath))
            {
                return Task.FromResult((Result)Result.Fail($"Destination folder already exists: {_destPath}"));
            }

            ResourceUtils.CopyFolder(_sourcePath, _destPath);
            _entityHelper.CopyFolderEntityDataFiles(_sourcePath, _destPath);

            return Task.FromResult(Result.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult((Result)Result.Fail($"Failed to copy folder: {_sourcePath} to {_destPath}")
                .WithException(ex));
        }
    }

    public override Task<Result> UndoAsync()
    {
        try
        {
            if (Directory.Exists(_destPath))
            {
                _entityHelper.DeleteFolderEntityDataFiles(_destPath);
                Directory.Delete(_destPath, recursive: true);
            }
            return Task.FromResult(Result.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult((Result)Result.Fail($"Failed to undo copy folder: {_destPath}")
                .WithException(ex));
        }
    }
}

/// <summary>
/// Undoable move folder operation.
/// </summary>
internal class MoveFolderOperation : FileOperation
{
    private readonly string _sourcePath;
    private readonly string _destPath;
    private readonly EntityFileHelper _entityHelper;

    public MoveFolderOperation(string sourcePath, string destPath, IEntityService? entityService, IResourceRegistry? resourceRegistry)
    {
        _sourcePath = sourcePath;
        _destPath = destPath;
        _entityHelper = new EntityFileHelper(entityService, resourceRegistry);
    }

    public override Task<Result> ExecuteAsync()
    {
        try
        {
            if (!Directory.Exists(_sourcePath))
            {
                return Task.FromResult((Result)Result.Fail($"Source folder does not exist: {_sourcePath}"));
            }

            // Allow move to same location with different case (case-only rename)
            bool isSameFolder = string.Equals(_sourcePath, _destPath, StringComparison.OrdinalIgnoreCase);
            if (Directory.Exists(_destPath) && !isSameFolder)
            {
                return Task.FromResult((Result)Result.Fail($"Destination folder already exists: {_destPath}"));
            }

            // Move entity data files first (while source folder still exists for enumeration)
            _entityHelper.MoveFolderEntityDataFiles(_sourcePath, _destPath);
            Directory.Move(_sourcePath, _destPath);

            return Task.FromResult(Result.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult((Result)Result.Fail($"Failed to move folder: {_sourcePath} to {_destPath}")
                .WithException(ex));
        }
    }

    public override Task<Result> UndoAsync()
    {
        try
        {
            if (!Directory.Exists(_destPath))
            {
                return Task.FromResult((Result)Result.Fail($"Folder no longer exists at destination: {_destPath}"));
            }

            // Move entity data files back first (while dest folder still exists for enumeration)
            _entityHelper.MoveFolderEntityDataFiles(_destPath, _sourcePath);
            Directory.Move(_destPath, _sourcePath);

            return Task.FromResult(Result.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult((Result)Result.Fail($"Failed to undo move folder: {_destPath} back to {_sourcePath}")
                .WithException(ex));
        }
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

    public override Task<Result> ExecuteAsync()
    {
        try
        {
            if (!Directory.Exists(_originalPath))
            {
                return Task.FromResult((Result)Result.Fail($"Folder does not exist: {_originalPath}"));
            }

            if (FileSystemHelper.IsDirectoryEmpty(_originalPath))
            {
                Directory.Delete(_originalPath);
            }
            else
            {
                // Move entity data files to trash first
                MoveEntityDataFilesToTrash();

                // Non-empty folder - move to trash
                FileSystemHelper.MoveDirectoryWithParentCreation(_originalPath, _trashPath);
            }

            return Task.FromResult(Result.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult((Result)Result.Fail($"Failed to delete folder: {_originalPath}")
                .WithException(ex));
        }
    }

    public override Task<Result> UndoAsync()
    {
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
                    return Task.FromResult((Result)Result.Fail($"Trash folder does not exist: {_trashPath}"));
                }

                FileSystemHelper.MoveDirectoryWithParentCreation(_trashPath, _originalPath);

                // Restore entity data files from trash
                RestoreEntityDataFiles();

                FileSystemHelper.CleanupEmptyParentDirectories(_trashPath);
            }

            return Task.FromResult(Result.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult((Result)Result.Fail($"Failed to restore deleted folder: {_originalPath}")
                .WithException(ex));
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

    private void MoveEntityDataFilesToTrash()
    {
        foreach (var (originalPath, trashPath) in _entityDataFiles)
        {
            if (File.Exists(originalPath))
            {
                FileSystemHelper.MoveFileWithDirectoryCreation(originalPath, trashPath);
                FileSystemHelper.CleanupEmptyParentDirectories(originalPath);
            }
        }
    }

    private void RestoreEntityDataFiles()
    {
        foreach (var (originalPath, trashPath) in _entityDataFiles)
        {
            if (File.Exists(trashPath))
            {
                FileSystemHelper.MoveFileWithDirectoryCreation(trashPath, originalPath);
                FileSystemHelper.CleanupEmptyParentDirectories(trashPath);
            }
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

    public override Task<Result> ExecuteAsync()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                return Task.FromResult((Result)Result.Fail($"File already exists: {_filePath}"));
            }

            var parentFolder = Path.GetDirectoryName(_filePath);
            if (!Directory.Exists(parentFolder))
            {
                return Task.FromResult((Result)Result.Fail($"Parent folder does not exist: {parentFolder}"));
            }

            File.WriteAllBytes(_filePath, _content);
            return Task.FromResult(Result.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult((Result)Result.Fail($"Failed to create file: {_filePath}")
                .WithException(ex));
        }
    }

    public override Task<Result> UndoAsync()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
            return Task.FromResult(Result.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult((Result)Result.Fail($"Failed to undo create file: {_filePath}")
                .WithException(ex));
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

    public override Task<Result> ExecuteAsync()
    {
        try
        {
            if (Directory.Exists(_folderPath))
            {
                return Task.FromResult((Result)Result.Fail($"Folder already exists: {_folderPath}"));
            }

            var parentFolder = Path.GetDirectoryName(_folderPath);
            if (!Directory.Exists(parentFolder))
            {
                return Task.FromResult((Result)Result.Fail($"Parent folder does not exist: {parentFolder}"));
            }

            Directory.CreateDirectory(_folderPath);
            return Task.FromResult(Result.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult((Result)Result.Fail($"Failed to create folder: {_folderPath}")
                .WithException(ex));
        }
    }

    public override Task<Result> UndoAsync()
    {
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
            return Task.FromResult(Result.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult((Result)Result.Fail($"Failed to undo create folder: {_folderPath}")
                .WithException(ex));
        }
    }
}
