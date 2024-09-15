using Celbridge.Utilities;
using Celbridge.Utilities.Services;
using Celbridge.Workspace;
using System.IO.Compression;

namespace Celbridge.Explorer.Services;

public class ResourceArchiver
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IResourceRegistry _resourceRegistry;
    private readonly IUtilityService _utilityService;

    private ResourceKey _resource;
    private string _archivePath = string.Empty;
    private bool _folderWasEmpty;
    private bool _folderWasExpanded;

    public ResourceType ArchivedResourceType { get; private set; }

    public ResourceArchiver(
        IWorkspaceWrapper workspaceWrapper,
        IUtilityService utilityService)
    {
        _workspaceWrapper = workspaceWrapper;
        _resourceRegistry = _workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;
        _utilityService = utilityService;
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
            var failure = Result.Fail($"Failed to archive resource: '{resource}'");
            failure.MergeErrors(getResult);
            return failure;
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
                archive.CreateEntryFromFile(deleteFilePath, resource);
            }

            File.Delete(deleteFilePath);
        }
        catch (Exception ex)
        {
            return Result.Fail(ex, $"An exception occured while archiving file: '{resource}'");
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
            return Result.Fail(ex, $"An exception occurred while unarchiving file: '{_resource}'");
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
            return Result.Fail(ex, $"An exception occurred while archiving folder: '{resource}'");
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
            return Result.Fail(ex, $"An exception occurred while unarchiving folder: {_resource}");
        }

        _resourceRegistry.SetFolderIsExpanded(_resource, _folderWasExpanded);

        ArchivedResourceType = ResourceType.Invalid;
        _folderWasEmpty = false;
        _folderWasExpanded = false;

        await Task.CompletedTask;

        return Result.Ok();
    }
}
