using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.Workspace;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Localization;
using System.Security.AccessControl;

namespace Celbridge.Project.Commands
{
    public class MoveResourceCommand : CommandBase, IMoveResourceCommand
    {
        public override string UndoStackName => UndoStackNames.Project;

        public ResourceKey FromResourceKey { get; set; }
        public ResourceKey ToResourceKey { get; set; }
        public bool ExpandMovedFolder { get; set; }

        private Type? _resourceType;

        private readonly IMessengerService _messengerService;
        private readonly IWorkspaceWrapper _workspaceWrapper;
        private readonly IProjectDataService _projectDataService;
        private readonly IDialogService _dialogService;
        private readonly IStringLocalizer _stringLocalizer;

        public MoveResourceCommand(
            IMessengerService messengerService,
            IWorkspaceWrapper workspaceWrapper,
            IProjectDataService projectDataService,
            IDialogService dialogService,
            IStringLocalizer stringLocalizer)
        {
            _messengerService = messengerService;
            _workspaceWrapper = workspaceWrapper;
            _projectDataService = projectDataService;
            _dialogService = dialogService;
            _stringLocalizer = stringLocalizer;
        }

        public override async Task<Result> ExecuteAsync()
        {
            if (!_workspaceWrapper.IsWorkspacePageLoaded)
            {
                return Result.Fail($"Failed to move resource. Workspace is not loaded");
            }

            // Determine the type of the resource being moved by checking if the file or folder exists.
            // We can't use the resource registry because if the file was moved in the Tree View then
            // the resource may have already been moved in the resource registry.

            var projectFolderPath = _projectDataService.LoadedProjectData!.ProjectFolderPath;
            if (string.IsNullOrEmpty(projectFolderPath))
            {
                return Result.Fail("Failed to move resource. Project folder path is empty.");
            }

            var resourcePath = Path.GetFullPath(Path.Combine(projectFolderPath, FromResourceKey));
            if (File.Exists(resourcePath))
            {
                _resourceType = typeof(IFileResource);
            }
            else if (Directory.Exists(resourcePath))
            {
                _resourceType = typeof(IFolderResource);
            }

            //
            // Move the resource
            //

            if (_resourceType == typeof(IFileResource))
            {
                var moveResult = await MoveFileInternal(FromResourceKey, ToResourceKey);
                if (moveResult.IsFailure)
                {
                    var titleString = _stringLocalizer.GetString("ResourceTree_MoveFile");
                    var messageString = _stringLocalizer.GetString("ResourceTree_MoveFileFailed", FromResourceKey, ToResourceKey);
                    await _dialogService.ShowAlertDialogAsync(titleString, messageString);

                    var message = new RequestResourceTreeUpdate();
                    _messengerService.Send(message);

                    return moveResult;
                }
            }
            else if (_resourceType == typeof(IFolderResource))
            {
                var moveResult = await MoveFolderInternal(FromResourceKey, ToResourceKey);
                if (moveResult.IsFailure)
                {
                    var titleString = _stringLocalizer.GetString("ResourceTree_MoveFolder");
                    var messageString = _stringLocalizer.GetString("ResourceTree_MoveFolderFailed", FromResourceKey, ToResourceKey);
                    await _dialogService.ShowAlertDialogAsync(titleString, messageString);

                    var message = new RequestResourceTreeUpdate();
                    _messengerService.Send(message);

                    return moveResult;
                }
            }
            else
            {
                return Result.Fail($"Unknown resource type for key: {FromResourceKey}");
            }

            return Result.Ok();
        }

        public override async Task<Result> UndoAsync()
        {
            if (!_workspaceWrapper.IsWorkspacePageLoaded)
            {
                return Result.Fail($"Failed to undo move resource. Workspace is not loaded");
            }

            if (_resourceType == typeof(IFileResource))
            {
                _resourceType = null;
                var moveResult = await MoveFileInternal(ToResourceKey, FromResourceKey);
                if (moveResult.IsFailure)
                {
                    var titleString = _stringLocalizer.GetString("ResourceTree_MoveFile");
                    var messageString = _stringLocalizer.GetString("ResourceTree_UndoMoveFileFailed", FromResourceKey, ToResourceKey);
                    await _dialogService.ShowAlertDialogAsync(titleString, messageString);

                    // Ensure the resource tree is up to date with the file system
                    var message = new RequestResourceTreeUpdate();
                    _messengerService.Send(message);

                    return moveResult;
                }
            }
            else if (_resourceType == typeof(IFolderResource))
            {
                _resourceType = null;
                var moveResult = await MoveFolderInternal(ToResourceKey, FromResourceKey);
                if (moveResult.IsFailure)
                {
                    var titleString = _stringLocalizer.GetString("ResourceTree_MoveFolder");
                    var messageString = _stringLocalizer.GetString("ResourceTree_UndoMoveFolderFailed", FromResourceKey, ToResourceKey);
                    await _dialogService.ShowAlertDialogAsync(titleString, messageString);

                    // Ensure the resource tree is up to date with the file system
                    var message = new RequestResourceTreeUpdate();
                    _messengerService.Send(message);

                    return moveResult;
                }
            }
            else
            {
                return Result.Fail($"Unknown resource type for key: {FromResourceKey}");
            }

            return Result.Ok();
        }

        private async Task<Result> MoveFileInternal(ResourceKey resourceKeyA, ResourceKey resourceKeyB)
        {
            if (resourceKeyA == resourceKeyB)
            {
                return Result.Fail($"Failed to move file. Source and destination are the same: '{resourceKeyA}'");
            }

            var loadedProjectData = _projectDataService.LoadedProjectData;
            Guard.IsNotNull(loadedProjectData);

            if (resourceKeyA.IsEmpty || resourceKeyB.IsEmpty)
            {
                return Result.Fail("Failed to move file. Resource key is empty.");
            }

            try
            {
                var projectFolderPath = loadedProjectData.ProjectFolderPath;
                var filePathA = Path.Combine(projectFolderPath, resourceKeyA);
                filePathA = Path.GetFullPath(filePathA);
                var filePathB = Path.Combine(projectFolderPath, resourceKeyB);
                filePathB = Path.GetFullPath(filePathB);

                if (!File.Exists(filePathA))
                {
                    return Result.Fail($"Failed to move file. File does not exist: {filePathA}");
                }

                if (File.Exists(filePathB))
                {
                    return Result.Fail($"Failed to move file. File already exists: {filePathB}");
                }

                var parentFolderPathB = Path.GetDirectoryName(filePathB);
                if (!Directory.Exists(parentFolderPathB))
                {
                    return Result.Fail($"Failed to move file. Target folder does not exist: {parentFolderPathB}");
                }

                File.Move(filePathA, filePathB);

                var resourceRegistry = _workspaceWrapper.WorkspaceService.ProjectService.ResourceRegistry;
                var newParentFolder = resourceKeyB.GetParent();
                if (!newParentFolder.IsEmpty)
                {
                    resourceRegistry.SetFolderIsExpanded(newParentFolder, true);
                }
            }
            catch (Exception ex)
            {
                return Result.Fail($"Failed to move file. {ex.Message}");
            }

            var message = new RequestResourceTreeUpdate();
            _messengerService.Send(message);

            await Task.CompletedTask;

            return Result.Ok();
        }

        private async Task<Result> MoveFolderInternal(ResourceKey resourceKeyA, ResourceKey resourceKeyB)
        {
            if (resourceKeyA == resourceKeyB)
            {
                return Result.Ok();
            }

            var workspaceService = _workspaceWrapper.WorkspaceService;
            var resourceRegistry = workspaceService.ProjectService.ResourceRegistry;
            var loadedProjectData = _projectDataService.LoadedProjectData;

            Guard.IsNotNull(loadedProjectData);

            if (resourceKeyA.IsEmpty || resourceKeyB.IsEmpty)
            {
                return Result.Fail("Failed to move folder. Resource key is empty.");
            }

            try
            {
                var projectFolderPath = loadedProjectData.ProjectFolderPath;
                var folderPathA = Path.Combine(projectFolderPath, resourceKeyA);
                folderPathA = Path.GetFullPath(folderPathA);
                var folderPathB = Path.Combine(projectFolderPath, resourceKeyB);
                folderPathB = Path.GetFullPath(folderPathB);

                if (!Directory.Exists(folderPathA))
                {
                    return Result.Fail($"Failed to move folder. Folder path does not exist: {folderPathA}");
                }

                if (Directory.Exists(folderPathB))
                {
                    return Result.Fail($"Failed to move folder. Folder path already exists: {folderPathB}");
                }

                Directory.Move(folderPathA, folderPathB);
            }
            catch (Exception ex)
            {
                return Result.Fail($"Failed to move folder. {ex.Message}");
            }

            var newParentFolder = resourceKeyB.GetParent();
            if (!newParentFolder.IsEmpty)
            {
                resourceRegistry.SetFolderIsExpanded(newParentFolder, true);
            }

            if (ExpandMovedFolder)
            {
                resourceRegistry.SetFolderIsExpanded(resourceKeyB, true);
            }

            var message = new RequestResourceTreeUpdate();
            _messengerService.Send(message);

            await Task.CompletedTask;

            return Result.Ok();
        }

        public static void MoveResource(ResourceKey fromResourceKey, ResourceKey toResourceKey)
        {
            var workspaceWrapper = ServiceLocator.ServiceProvider.GetRequiredService<IWorkspaceWrapper>();
            if (!workspaceWrapper.IsWorkspacePageLoaded)
            {
                return;
            }

            // If toResourceKey specifies an existing folder, then we assume that the user
            // intended to move the resource to that folder.

            var resourceRegistry = workspaceWrapper.WorkspaceService.ProjectService.ResourceRegistry;
            var getResult = resourceRegistry.GetResource(toResourceKey);
            if (getResult.IsSuccess)
            {
                var resource = getResult.Value;
                if (resource is IFolderResource)
                {
                    toResourceKey += "/" + Path.GetFileName(fromResourceKey);
                }
            }

            // Execute the move resource command

            var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
            commandService.Execute<IMoveResourceCommand>(command =>
            {
                command.FromResourceKey = fromResourceKey;
                command.ToResourceKey = toResourceKey;
            });
        }
    }
}
