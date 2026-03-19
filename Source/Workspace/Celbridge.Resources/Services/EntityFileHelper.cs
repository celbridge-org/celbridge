using Celbridge.Entities;

namespace Celbridge.Resources.Services;

/// <summary>
/// Helper class for performing file operations that preserve associated entity data files.
/// </summary>
internal class EntityFileHelper
{
    private readonly IEntityService? _entityService;
    private readonly IResourceRegistry? _resourceRegistry;

    public EntityFileHelper(IEntityService? entityService, IResourceRegistry? resourceRegistry)
    {
        _entityService = entityService;
        _resourceRegistry = resourceRegistry;
    }

    /// <summary>
    /// Returns true if entity services are available.
    /// </summary>
    public bool HasEntityServices => _entityService != null && _resourceRegistry != null;

    /// <summary>
    /// Gets the resource key for a file path if possible.
    /// </summary>
    public ResourceKey? GetResourceKey(string path)
    {
        if (_resourceRegistry == null)
        {
            return null;
        }

        var result = _resourceRegistry.GetResourceKey(path);
        return result.IsSuccess ? result.Value : default;
    }

    /// <summary>
    /// Gets the entity data file path for a resource if one exists.
    /// </summary>
    public string? GetEntityDataPath(ResourceKey resourceKey)
    {
        if (_entityService == null)
        {
            return null;
        }

        var path = _entityService.GetEntityDataPath(resourceKey);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Gets the relative entity data file path for a resource.
    /// </summary>
    public string? GetEntityDataRelativePath(ResourceKey resourceKey)
    {
        return _entityService?.GetEntityDataRelativePath(resourceKey);
    }

    /// <summary>
    /// Copies the entity data file for a single file if it exists.
    /// </summary>
    public void CopyEntityDataFile(string sourcePath, string destPath)
    {
        if (_entityService == null || _resourceRegistry == null)
        {
            return;
        }

        var sourceKeyResult = _resourceRegistry.GetResourceKey(sourcePath);
        var destKeyResult = _resourceRegistry.GetResourceKey(destPath);

        if (sourceKeyResult.IsSuccess && destKeyResult.IsSuccess)
        {
            _entityService.CopyEntityDataFile(sourceKeyResult.Value, destKeyResult.Value);
        }
    }

    /// <summary>
    /// Moves the entity data file for a single file if it exists.
    /// </summary>
    public void MoveEntityDataFile(string sourcePath, string destPath)
    {
        if (_entityService == null || _resourceRegistry == null)
        {
            return;
        }

        var sourceKeyResult = _resourceRegistry.GetResourceKey(sourcePath);
        var destKeyResult = _resourceRegistry.GetResourceKey(destPath);

        if (sourceKeyResult.IsSuccess && destKeyResult.IsSuccess)
        {
            _entityService.MoveEntityDataFile(sourceKeyResult.Value, destKeyResult.Value);
        }
    }

    /// <summary>
    /// Deletes the entity data file for a single file if it exists.
    /// </summary>
    public void DeleteEntityDataFile(string resourcePath)
    {
        if (_entityService == null || _resourceRegistry == null)
        {
            return;
        }

        var resourceKeyResult = _resourceRegistry.GetResourceKey(resourcePath);
        if (resourceKeyResult.IsFailure)
        {
            return;
        }

        var entityDataPath = _entityService.GetEntityDataPath(resourceKeyResult.Value);
        if (File.Exists(entityDataPath))
        {
            File.Delete(entityDataPath);
            CleanupEmptyEntityDataDirectories(entityDataPath);
        }
    }

    /// <summary>
    /// Copies entity data files for all files within a folder recursively.
    /// </summary>
    public void CopyFolderEntityDataFiles(string sourceFolderPath, string destFolderPath)
    {
        if (_entityService == null || _resourceRegistry == null)
        {
            return;
        }

        if (!Directory.Exists(sourceFolderPath))
        {
            return;
        }

        // Copy entity data for all files in the source folder
        foreach (var sourceFile in Directory.GetFiles(sourceFolderPath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceFolderPath, sourceFile);
            var destFile = Path.Combine(destFolderPath, relativePath);
            CopyEntityDataFile(sourceFile, destFile);
        }
    }

    /// <summary>
    /// Moves entity data files for all files within a folder recursively.
    /// </summary>
    public void MoveFolderEntityDataFiles(string sourceFolderPath, string destFolderPath)
    {
        if (_entityService == null || _resourceRegistry == null)
        {
            return;
        }

        if (!Directory.Exists(sourceFolderPath))
        {
            return;
        }

        // Move entity data for all files in the source folder
        foreach (var sourceFile in Directory.GetFiles(sourceFolderPath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceFolderPath, sourceFile);
            var destFile = Path.Combine(destFolderPath, relativePath);
            MoveEntityDataFile(sourceFile, destFile);
        }
    }

    /// <summary>
    /// Deletes entity data files for all files within a folder recursively.
    /// </summary>
    public void DeleteFolderEntityDataFiles(string folderPath)
    {
        if (_entityService == null || _resourceRegistry == null)
        {
            return;
        }

        if (!Directory.Exists(folderPath))
        {
            return;
        }

        foreach (var filePath in Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories))
        {
            DeleteEntityDataFile(filePath);
        }
    }

    /// <summary>
    /// Moves entity data files for a folder to a trash location, preserving the structure for undo.
    /// Returns a list of (originalPath, trashPath) pairs for entity data files that were moved.
    /// </summary>
    public List<(string OriginalPath, string TrashPath)> MoveFolderEntityDataFilesToTrash(
        string folderPath, 
        string trashBasePath,
        string projectFolderPath)
    {
        var movedFiles = new List<(string OriginalPath, string TrashPath)>();

        if (_entityService == null || _resourceRegistry == null)
        {
            return movedFiles;
        }

        if (!Directory.Exists(folderPath))
        {
            return movedFiles;
        }

        foreach (var filePath in Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories))
        {
            var resourceKeyResult = _resourceRegistry.GetResourceKey(filePath);
            if (resourceKeyResult.IsFailure)
            {
                continue;
            }

            var entityDataPath = _entityService.GetEntityDataPath(resourceKeyResult.Value);
            if (!File.Exists(entityDataPath))
            {
                continue;
            }

            var entityDataRelativePath = _entityService.GetEntityDataRelativePath(resourceKeyResult.Value);
            var entityDataTrashPath = Path.Combine(trashBasePath, entityDataRelativePath);

            var entityDataTrashDir = Path.GetDirectoryName(entityDataTrashPath)!;
            Directory.CreateDirectory(entityDataTrashDir);
            File.Move(entityDataPath, entityDataTrashPath);

            movedFiles.Add((entityDataPath, entityDataTrashPath));

            // Clean up empty directories in the entity data folder
            CleanupEmptyEntityDataDirectories(entityDataPath);
        }

        return movedFiles;
    }

    /// <summary>
    /// Restores entity data files from trash to their original locations.
    /// </summary>
    public void RestoreEntityDataFilesFromTrash(List<(string OriginalPath, string TrashPath)> trashedFiles)
    {
        foreach (var (originalPath, trashPath) in trashedFiles)
        {
            if (!File.Exists(trashPath))
            {
                continue;
            }

            var originalDir = Path.GetDirectoryName(originalPath)!;
            Directory.CreateDirectory(originalDir);
            File.Move(trashPath, originalPath);
        }
    }

    /// <summary>
    /// Cleans up empty parent directories after deleting an entity data file.
    /// </summary>
    private void CleanupEmptyEntityDataDirectories(string entityDataPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(entityDataPath);
            while (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                if (Directory.GetFiles(dir).Length == 0 && Directory.GetDirectories(dir).Length == 0)
                {
                    Directory.Delete(dir);
                    dir = Path.GetDirectoryName(dir);
                }
                else
                {
                    break;
                }
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }
}
