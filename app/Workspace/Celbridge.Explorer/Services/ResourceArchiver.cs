using Celbridge.Entities;
using Celbridge.Utilities;
using Celbridge.Utilities.Services;
using Celbridge.Workspace;
using System.IO.Compression;

namespace Celbridge.Explorer.Services;

/// <summary>
/// Archives resources by storing a copy of the resource and then deleting it.
/// The previously archived resource can later be unarchived back to the original location, e.g. as part of an undo operation.
/// </summary>
public class ResourceArchiver
{
    private readonly IResourceRegistry _resourceRegistry;
    private readonly IEntityService _entityService;
    private readonly IUtilityService _utilityService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    private ResourceKey _resource;
    private string _archivePath = string.Empty;
    private bool _folderWasEmpty;
    private bool _folderWasExpanded;

    public ResourceType ArchivedResourceType { get; private set; }

    public ResourceArchiver(
        IUtilityService utilityService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _utilityService = utilityService;
        _workspaceWrapper = workspaceWrapper;
        _resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        _entityService = _workspaceWrapper.WorkspaceService.EntityService;
    }

    public async Task<Result> ArchiveResourceAsync(ResourceKey resource)
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Workspace is not loaded");
        }

        if (resource.IsEmpty)
        {
            return Result.Fail("Resource key is empty");
        }

        var getResult = _resourceRegistry.GetResource(resource);
        if (getResult.IsFailure)
        {
            return Result.Fail($"Failed to archive resource: '{resource}'")
                .WithErrors(getResult);
        }

        var r = getResult.Value;
        if (r is IFileResource)
        {
            return await ArchiveFileAsync(resource);
        }
        else if (r is IFolderResource)
        {
            return await ArchiveFolderAsync(resource);
        }

        throw new InvalidOperationException("Invalid resource type");
    }

    public async Task<Result> UnarchiveResourceAsync()
    {
        if (ArchivedResourceType == ResourceType.File)
        {
            return await UnarchiveFileAsync();
        }
        else if (ArchivedResourceType == ResourceType.Folder)
        {
            return await UnarchiveFolderAsync();
        }

        throw new InvalidOperationException("Invalid resource type");
    }

    private async Task<Result> ArchiveFileAsync(ResourceKey resource)
    {
        try
        {
            var deleteFilePath = _resourceRegistry.GetResourcePath(resource);

            if (!File.Exists(deleteFilePath))
            {
                return Result.Fail($"File does not exist: {deleteFilePath}");
            }

            _archivePath = _utilityService.GetTemporaryFilePath(PathConstants.DeletedFilesFolder, ".zip");
            if (File.Exists(_archivePath))
            {
                File.Delete(_archivePath);
            }

            var archiveFolderPath = Path.GetDirectoryName(_archivePath)!;
            Directory.CreateDirectory(archiveFolderPath);

            using (var archive = ZipFile.Open(_archivePath, ZipArchiveMode.Create))
            {
                // Add the resource to the archive.
                archive.CreateEntryFromFile(deleteFilePath, resource);

                // Add the associated EntityData file, if one exists, to the archive.
                var entityDataPath = _entityService.GetEntityDataPath(resource);
                if (File.Exists(entityDataPath))
                {
                    // Add the EntityData file using the project relative path as the entry name,
                    // so that it will be extracted to the correct location on undo.
                    var entityDataRelativePath = _entityService.GetEntityDataRelativePath(resource);
                    archive.CreateEntryFromFile(entityDataPath, entityDataRelativePath);
                }
            }

            // Delete the resource. The associated EntityData file, if one exists, will be deleted
            // during the following resource registry update.
            File.Delete(deleteFilePath);
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occured while archiving file: '{resource}'")
                .WithException(ex);
        }

        // Record that the file resource was archived so we can unarchive it later
        ArchivedResourceType = ResourceType.File;
        _resource = resource;

        await Task.CompletedTask;

        return Result.Ok();
    }

    private async Task<Result> UnarchiveFileAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to unarchive file. Workspace is not loaded");
        }

        if (!File.Exists(_archivePath))
        {
            return Result.Fail($"Failed to unarchive file. Archive does not exist: {_archivePath}");
        }

        var projectFolderPath = _resourceRegistry.GetResourcePath(_resourceRegistry.RootFolder);

        try
        {
            ZipFile.ExtractToDirectory(_archivePath, projectFolderPath);
            File.Delete(_archivePath);
            _archivePath = string.Empty;
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred while unarchiving file: '{_resource}'")
                .WithException(ex);
        }

        ArchivedResourceType = ResourceType.Invalid;

        await Task.CompletedTask;

        return Result.Ok();
    }

    private async Task<Result> ArchiveFolderAsync(ResourceKey resource)
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Workspace is not loaded");
        }

        if (resource.IsEmpty)
        {
            // Note: Deleting the root folder is not permitted (referred to by an empty string key)
            return Result.Fail("Resource key is empty");
        }

        var getResult = _resourceRegistry.GetResource(resource);
        if (getResult.IsFailure)
        {
            return Result.Fail($"Failed to get folder resource: '{resource}'");
        }
        var r = getResult.Value;

        if (r is not IFolderResource)
        {
            return Result.Fail($"Resource is not a folder resource: '{resource}'");
        }

        try
        {
            var deleteFolderPath = _resourceRegistry.GetResourcePath(resource);

            if (!Directory.Exists(deleteFolderPath))
            {
                return Result.Fail($"Folder does not exist: {deleteFolderPath}");
            }

            var files = Directory.GetFiles(deleteFolderPath);
            var directories = Directory.GetDirectories(deleteFolderPath);

            if (files.Length == 0 && directories.Length == 0)
            {
                _folderWasEmpty = true;
            }
            else
            {
                _archivePath = _utilityService.GetTemporaryFilePath(PathConstants.DeletedFilesFolder, ".zip");
                if (File.Exists(_archivePath))
                {
                    File.Delete(_archivePath);
                }

                var archiveFolderPath = Path.GetDirectoryName(_archivePath)!;
                Directory.CreateDirectory(archiveFolderPath);

                ZipFile.CreateFromDirectory(deleteFolderPath, _archivePath, CompressionLevel.Optimal, includeBaseDirectory: false);
            }

            _folderWasExpanded = _resourceRegistry.IsFolderExpanded(resource);

            Directory.Delete(deleteFolderPath, true);
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred while archiving folder: '{resource}'")
                .WithException(ex);
        }

        await Task.CompletedTask;

        // Record that the folder resource was archived so we can undo it later
        ArchivedResourceType = ResourceType.Folder;
        _resource = resource;

        return Result.Ok();
    }

    private async Task<Result> UnarchiveFolderAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Workspace is not loaded");
        }

        try
        {
            var folderPath = _resourceRegistry.GetResourcePath(_resource);

            if (_folderWasEmpty)
            {
                Directory.CreateDirectory(folderPath);
                _folderWasEmpty = false;
            }
            else
            {
                if (!File.Exists(_archivePath))
                {
                    return Result.Fail($"Archive does not exist: {_archivePath}");
                }

                ZipFile.ExtractToDirectory(_archivePath, folderPath);
                File.Delete(_archivePath);
                _archivePath = string.Empty;
            }
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred while unarchiving folder: {_resource}")
                .WithException(ex);
        }

        _resourceRegistry.SetFolderIsExpanded(_resource, _folderWasExpanded);

        ArchivedResourceType = ResourceType.Invalid;
        _folderWasEmpty = false;
        _folderWasExpanded = false;

        await Task.CompletedTask;

        return Result.Ok();
    }
}
