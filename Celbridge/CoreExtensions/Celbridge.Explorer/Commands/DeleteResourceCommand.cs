using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Projects;
using Celbridge.Utilities.Services;
using Celbridge.Utilities;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Localization;
using System.IO.Compression;

namespace Celbridge.Explorer.Commands
{
    public class DeleteResourceCommand : CommandBase, IDeleteResourceCommand
    {
        public override string UndoStackName => UndoStackNames.Project;
        public override CommandFlags CommandFlags => CommandFlags.UpdateResources;

        public ResourceKey Resource { get; set; }

        private Type? _deletedResourceType;
        private string _archivePath = string.Empty;
        private bool _folderWasEmpty;
        private bool _folderWasExpanded;

        private readonly IWorkspaceWrapper _workspaceWrapper;
        private readonly IProjectService _projectService;
        private readonly IUtilityService _utilityService;
        private readonly IDialogService _dialogService;
        private readonly IStringLocalizer _stringLocalizer;

        public DeleteResourceCommand(
            IWorkspaceWrapper workspaceWrapper,
            IProjectService projectService,
            IUtilityService utilityService,
            IDialogService dialogService,
            IStringLocalizer stringLocalizer)
        {
            _workspaceWrapper = workspaceWrapper;
            _projectService = projectService;
            _utilityService = utilityService;
            _dialogService = dialogService;
            _stringLocalizer = stringLocalizer;
        }

        public override async Task<Result> ExecuteAsync()
        {
            var resourceRegistry = _workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;

            var getResourceResult = resourceRegistry.GetResource(Resource);
            if (getResourceResult.IsFailure)
            {
                return Result.Fail($"Failed to get resource: {Resource}");
            }

            var resource = getResourceResult.Value;

            if (resource is IFileResource)
            {
                var deleteResult = await DeleteFileAsync();
                if (deleteResult.IsFailure)
                {
                    var titleString = _stringLocalizer.GetString("ResourceTree_DeleteFile");
                    var messageString = _stringLocalizer.GetString("ResourceTree_DeleteFileFailed", Resource);
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
                    var messageString = _stringLocalizer.GetString("ResourceTree_DeleteFolderFailed", Resource);
                    await _dialogService.ShowAlertDialogAsync(titleString, messageString);

                    return deleteResult;
                }
            }
            else
            {
                return Result.Fail($"Unknown resource type for key: {Resource}");
            }

            return Result.Ok();
        }

        public override async Task<Result> UndoAsync()
        {
            var resourceRegistry = _workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;

            if (_deletedResourceType == typeof(IFileResource))
            {
                _deletedResourceType = null;
                var undoResult = await UndoDeleteFileAsync();
                if (undoResult.IsFailure)
                {
                    var titleString = _stringLocalizer.GetString("ResourceTree_DeleteFile");
                    var messageString = _stringLocalizer.GetString("ResourceTree_UndoDeleteFileFailed", Resource);
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
                    var messageString = _stringLocalizer.GetString("ResourceTree_UndoDeleteFolderFailed", Resource);
                    await _dialogService.ShowAlertDialogAsync(titleString, messageString);

                    return undoResult;
                }
            }
            else
            {
                return Result.Fail($"Unknown resource type for key: {Resource}");
            }

            return Result.Ok();
        }

        private async Task<Result> DeleteFileAsync()
        {
            if (!_workspaceWrapper.IsWorkspacePageLoaded)
            {
                return Result.Fail($"Failed to delete file. Workspace is not loaded");
            }

            var loadedProject = _projectService.LoadedProject;
            Guard.IsNotNull(loadedProject);

            if (Resource.IsEmpty)
            {
                return Result.Fail("Failed to delete file. Resource key is empty");
            }

            try
            {
                var projectFolderPath = loadedProject.ProjectFolderPath;
                var deleteFilePath = Path.Combine(projectFolderPath, Resource);
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
                    archive.CreateEntryFromFile(deleteFilePath, Resource);
                }

                File.Delete(deleteFilePath);
            }
            catch (Exception ex)
            {
                return Result.Fail($"Failed to delete file. {ex.Message}");
            }

            // Record that a file was deleted
            _deletedResourceType = typeof(IFileResource);

            await Task.CompletedTask;

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

            var loadedProject = _projectService.LoadedProject;
            Guard.IsNotNull(loadedProject);

            var projectFolderPath = loadedProject.ProjectFolderPath;

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

            await Task.CompletedTask;

            return Result.Ok();
        }

        private async Task<Result> DeleteFolderAsync()
        {
            if (!_workspaceWrapper.IsWorkspacePageLoaded)
            {
                return Result.Fail($"Failed to delete folder. Workspace is not loaded");
            }

            var loadedProject = _projectService.LoadedProject;
            Guard.IsNotNull(loadedProject);

            if (Resource.IsEmpty)
            {
                return Result.Fail("Failed to delete folder. Resource key is empty");
            }

            try
            {
                var projectFolderPath = loadedProject.ProjectFolderPath;
                var deleteFolderPath = Path.Combine(projectFolderPath, Resource);
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

                _folderWasExpanded = _workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry.IsFolderExpanded(Resource);

                Directory.Delete(deleteFolderPath, true);
            }
            catch (Exception ex)
            {
                return Result.Fail($"Failed to delete folder. {ex.Message}");
            }

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

            var loadedProject = _projectService.LoadedProject;
            Guard.IsNotNull(loadedProject);

            var projectFolderPath = loadedProject.ProjectFolderPath;

            try
            {
                var folderPath = Path.GetFullPath(Path.Combine(projectFolderPath, Resource));

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

            _workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry.SetFolderIsExpanded(Resource, _folderWasExpanded);

            await Task.CompletedTask;

            return Result.Ok();
        }

        //
        // Static methods for scripting support.
        //

        public static void DeleteResource(ResourceKey resource)
        {
            var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
            commandService.Execute<IDeleteResourceCommand>(command => command.Resource = resource);
        }
    }
}
