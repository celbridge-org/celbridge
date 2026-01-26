using Celbridge.DataTransfer;

namespace Celbridge.Explorer;

/// <summary>
/// Service for performing file system operations with undo/redo support.
/// </summary>
public interface IFileOperationService
{
    /// <summary>
    /// Create a new file with the specified content.
    /// </summary>
    Task<Result> CreateFileAsync(string path, byte[] content);

    /// <summary>
    /// Create a new empty folder.
    /// </summary>
    Task<Result> CreateFolderAsync(string path);

    /// <summary>
    /// Copy a file from source to destination path.
    /// </summary>
    Task<Result> CopyFileAsync(string sourcePath, string destPath);

    /// <summary>
    /// Move a file from source to destination path.
    /// </summary>
    Task<Result> MoveFileAsync(string sourcePath, string destPath);

    /// <summary>
    /// Delete a file at the specified path.
    /// </summary>
    Task<Result> DeleteFileAsync(string path);

    /// <summary>
    /// Copy a folder from source to destination path.
    /// </summary>
    Task<Result> CopyFolderAsync(string sourcePath, string destPath);

    /// <summary>
    /// Move a folder from source to destination path.
    /// </summary>
    Task<Result> MoveFolderAsync(string sourcePath, string destPath);

    /// <summary>
    /// Delete a folder at the specified path.
    /// </summary>
    Task<Result> DeleteFolderAsync(string path);

    /// <summary>
    /// Transfer a file or folder from source to destination path.
    /// </summary>
    Task<Result> TransferAsync(string sourcePath, string destPath, DataTransferMode mode);

    /// <summary>
    /// Begin a batch of operations that will be grouped together as a single undo unit.
    /// Call CommitBatch() when done to finalize the batch.
    /// </summary>
    void BeginBatch();

    /// <summary>
    /// Commit the current batch of operations as a single undo unit.
    /// </summary>
    void CommitBatch();

    /// <summary>
    /// Returns true if there are operations that can be undone.
    /// </summary>
    bool CanUndo { get; }

    /// <summary>
    /// Returns true if there are operations that can be redone.
    /// </summary>
    bool CanRedo { get; }

    /// <summary>
    /// Undo the most recent operation or batch of operations.
    /// </summary>
    Task<Result> UndoAsync();

    /// <summary>
    /// Redo the most recently undone operation or batch of operations.
    /// </summary>
    Task<Result> RedoAsync();
}
