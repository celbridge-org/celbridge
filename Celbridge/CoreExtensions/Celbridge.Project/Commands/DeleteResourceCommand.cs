using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.Utilities;
using Celbridge.BaseLibrary.Workspace;
using Celbridge.Utilities.Services;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Localization;
using System.IO.Compression;

namespace Celbridge.Project.Commands
{
    public class DeleteResourceCommand : CommandBase, IDeleteResourceCommand
    {
        public override string UndoStackName => UndoStackNames.Project;

        public ResourceKey ResourceKey { get; set; }

        private Type? _deletedResourceType;
        private string _archivePath = string.Empty;
        private bool _folderWasEmpty;
        private bool _folderWasExpanded;

        private readonly IMessengerService _messengerService;
        private readonly IWorkspaceWrapper _workspaceWrapper;
        private readonly IProjectDataService _projectDataService;
        private readonly IUtilityService _utilityService;
        private readonly IDialogService _dialogService;
        private readonly IStringLocalizer _stringLocalizer;

        public DeleteResourceCommand(
            IMessengerService messengerService,
            IWorkspaceWrapper workspaceWrapper,
            IProjectDataService projectDataService,
            IUtilityService utilityService,
            IDialogService dialogService,
            IStringLocalizer stringLocalizer)
        {
            _messengerService = messengerService;
            _workspaceWrapper = workspaceWrapper;
            _projectDataService = projectDataService;
            _utilityService = utilityService;
            _dialogService = dialogService;
            _stringLocalizer = stringLocalizer;
        }

        public override async Task<Result> ExecuteAsync()
        {
            var resourceRegistry = _workspaceWrapper.WorkspaceService.ProjectService.ResourceRegistry;

            var getResourceResult = resourceRegistry.GetResource(ResourceKey);
            if (getResourceResult.IsFailure)
            {
                return Result.Fail($"Failed to get resource: {ResourceKey}");
            }

            var resource = getResourceResult.Value;

            if (resource is IFileResource)
            {
                var deleteResult = await DeleteFileAsync();
                if (deleteResult.IsFailure)
                {
                    var titleString = _stringLocalizer.GetString("ResourceTree_DeleteFile");
                    var messageString = _stringLocalizer.GetString("ResourceTree_DeleteFileFailed", ResourceKey);
                    await _dialogService.ShowAlertDialogAsync(titleString, messageString);

                    return deleteResult;
                }
            }
            else if (resource is IFolderResource)
            {
                var deleteResult = await DeleteFolderAsync();
                if (deleteResult.IsFailure)
                {
                    var titleString = _stringLocalizer.GetString("ResourceTree_DeleteFolder");
                    var messageString = _stringLocalizer.GetString("ResourceTree_DeleteFolderFailed", ResourceKey);
                    await _dialogService.ShowAlertDialogAsync(titleString, messageString);

                    return deleteResult;
                }
            }
            else
            {
                return Result.Fail($"Unknown resource type for key: {ResourceKey}");
            }

            return Result.Ok();
        }

        public override async Task<Result> UndoAsync()
        {
            var resourceRegistry = _workspaceWrapper.WorkspaceService.ProjectService.ResourceRegistry;

            if (_deletedResourceType == typeof(IFileResource))
            {
                _deletedResourceType = null;
                var undoResult = await UndoDeleteFileAsync();
                if (undoResult.IsFailure)
                {
                    var titleString = _stringLocalizer.GetString("ResourceTree_DeleteFile");
                    var messageString = _stringLocalizer.GetString("ResourceTree_UndoDeleteFileFailed", ResourceKey);
                    await _dialogService.ShowAlertDialogAsync(titleString, messageString);

                    return undoResult;
                }
            }
            else if (_deletedResourceType == typeof(IFolderResource))
            {
                _deletedResourceType = null;
                var undoResult = await UndoDeleteFolderAsync();
                if (undoResult.IsFailure)
                {
                    var titleString = _stringLocalizer.GetString("ResourceTree_DeleteFolder");
                    var messageString = _stringLocalizer.GetString("ResourceTree_UndoDeleteFolderFailed", ResourceKey);
                    await _dialogService.ShowAlertDialogAsync(titleString, messageString);

                    return undoResult;
                }
            }
            else
            {
                return Result.Fail($"Unknown resource type for key: {ResourceKey}");
            }

            return Result.Ok();
        }

        private async Task<Result> DeleteFileAsync()
        {
            if (!_workspaceWrapper.IsWorkspacePageLoaded)
            {
                return Result.Fail($"Failed to delete file. Workspace is not loaded");
            }

            var loadedProjectData = _projectDataService.LoadedProjectData;
            Guard.IsNotNull(loadedProjectData);

            if (ResourceKey.IsEmpty)
            {
                return Result.Fail("Failed to delete file. Resource key is empty");
            }

            try
            {
                var projectFolderPath = loadedProjectData.ProjectFolderPath;
                var deleteFilePath = Path.Combine(projectFolderPath, ResourceKey);
                deleteFilePath = Path.GetFullPath(deleteFilePath);

                if (!File.Exists(deleteFilePath))
                {
                    return Result.Fail($"Failed to delete file. File does not exist: {deleteFilePath}");
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
                    archive.CreateEntryFromFile(deleteFilePath, ResourceKey);
                }

                File.Delete(deleteFilePath);
            }
            catch (Exception ex)
            {
                return Result.Fail($"Failed to delete file. {ex.Message}");
            }

            _messengerService.Send(new RequestResourceTreeUpdate());
            await Task.CompletedTask;

            // Record that a file was deleted
            _deletedResourceType = typeof(IFileResource);

            return Result.Ok();
        }

        private async Task<Result> UndoDeleteFileAsync()
        {
            if (!_workspaceWrapper.IsWorkspacePageLoaded)
            {
                return Result.Fail($"Failed to undo file delete. Workspace is not loaded");
            }

            if (!File.Exists(_archivePath))
            {
                return Result.Fail($"Failed to undo file delete. Archive does not exist: {_archivePath}");
            }

            var loadedProjectData = _projectDataService.LoadedProjectData;
            Guard.IsNotNull(loadedProjectData);

            var projectFolderPath = loadedProjectData.ProjectFolderPath;

            try
            {
                ZipFile.ExtractToDirectory(_archivePath, projectFolderPath);
                File.Delete(_archivePath);
                _archivePath = string.Empty;
            }
            catch (Exception ex)
            {
                return Result.Fail($"Failed to undo file delete. {ex.Message}");
            }

            _messengerService.Send(new RequestResourceTreeUpdate());
            await Task.CompletedTask;

            return Result.Ok();
        }

        private async Task<Result> DeleteFolderAsync()
        {
            if (!_workspaceWrapper.IsWorkspacePageLoaded)
            {
                return Result.Fail($"Failed to delete folder. Workspace is not loaded");
            }

            var loadedProjectData = _projectDataService.LoadedProjectData;
            Guard.IsNotNull(loadedProjectData);

            if (ResourceKey.IsEmpty)
            {
                return Result.Fail("Failed to delete folder. Resource key is empty");
            }

            try
            {
                var projectFolderPath = loadedProjectData.ProjectFolderPath;
                var deleteFolderPath = Path.Combine(projectFolderPath, ResourceKey);
                deleteFolderPath = Path.GetFullPath(deleteFolderPath);

                if (!Directory.Exists(deleteFolderPath))
                {
                    return Result.Fail($"Failed to delete folder. Folder does not exist: {deleteFolderPath}");
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

                _folderWasExpanded = _workspaceWrapper.WorkspaceService.ProjectService.ResourceRegistry.IsFolderExpanded(ResourceKey);

                Directory.Delete(deleteFolderPath, true);
            }
            catch (Exception ex)
            {
                return Result.Fail($"Failed to delete folder. {ex.Message}");
            }

            _messengerService.Send(new RequestResourceTreeUpdate());
            await Task.CompletedTask;

            // Record that a file was deleted
            _deletedResourceType = typeof(IFolderResource);

            return Result.Ok();
        }

        private async Task<Result> UndoDeleteFolderAsync()
        {
            if (!_workspaceWrapper.IsWorkspacePageLoaded)
            {
                return Result.Fail($"Failed to undo folder delete. Workspace is not loaded");
            }

            var loadedProjectData = _projectDataService.LoadedProjectData;
            Guard.IsNotNull(loadedProjectData);

            var projectFolderPath = loadedProjectData.ProjectFolderPath;

            try
            {
                var folderPath = Path.GetFullPath(Path.Combine(projectFolderPath, ResourceKey));

                if (_folderWasEmpty)
                {
                    Directory.CreateDirectory(folderPath);
                    _folderWasEmpty = false;
                }
                else
                {
                    if (!File.Exists(_archivePath))
                    {
                        return Result.Fail($"Failed to undo folder delete. Archive does not exist: {_archivePath}");
                    }

                    ZipFile.ExtractToDirectory(_archivePath, folderPath);
                    File.Delete(_archivePath);
                    _archivePath = string.Empty;
                }
            }
            catch (Exception ex)
            {
                return Result.Fail($"Failed to undo folder delete. {ex.Message}");
            }

            _workspaceWrapper.WorkspaceService.ProjectService.ResourceRegistry.SetFolderIsExpanded(ResourceKey, _folderWasExpanded);
            _messengerService.Send(new RequestResourceTreeUpdate());
            await Task.CompletedTask;

            return Result.Ok();
        }

        //
        // Static methods for scripting support.
        //

        public static void DeleteResource(string resourceKey)
        {
            var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
            commandService.Execute<IDeleteResourceCommand>(command => command.ResourceKey = resourceKey);
        }
    }
}
