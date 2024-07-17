using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.Workspace;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Localization;

namespace Celbridge.Project.Commands
{
    public class CopyResourceCommand : CommandBase, ICopyResourceCommand
    {
        public override string UndoStackName => UndoStackNames.Project;

        public ResourceKey SourceResourceKey { get; set; }
        public ResourceKey DestResourceKey { get; set; }
        public CopyResourceOperation Operation { get; set; }
        public bool ExpandCopiedFolder { get; set; }

        private readonly IMessengerService _messengerService;
        private readonly IWorkspaceWrapper _workspaceWrapper;
        private readonly IProjectDataService _projectDataService;
        private readonly IDialogService _dialogService;
        private readonly IStringLocalizer _stringLocalizer;

        private Type? _resourceType;
        private ResourceKey _resolvedDestination;
        private List<string> _copiedFilePaths = new();
        private List<string> _copiedFolderPaths = new();

        public CopyResourceCommand(
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
                return Result.Fail($"Workspace is not loaded");
            }

            // Determine the type of the resource being copied by checking if the file or folder exists.
            // Note: We can't use the resource registry to check this because if the user moved the file
            // in the Tree View then the resource may have already been moved in the resource registry.

            var projectFolderPath = _projectDataService.LoadedProjectData!.ProjectFolderPath;
            if (string.IsNullOrEmpty(projectFolderPath))
            {
                return Result.Fail("Project folder path is empty.");
            }

            var resourcePath = Path.GetFullPath(Path.Combine(projectFolderPath, SourceResourceKey));
            if (File.Exists(resourcePath))
            {
                _resourceType = typeof(IFileResource);
            }
            else if (Directory.Exists(resourcePath))
            {
                _resourceType = typeof(IFolderResource);
            }

            //
            // Copy the resource
            //

            // Resolve references to destination resource
            _resolvedDestination = ResolveDestinationResourceKey(SourceResourceKey, DestResourceKey);

            if (_resourceType == typeof(IFileResource))
            {
                var copyResult = await CopyFileInternal(SourceResourceKey, _resolvedDestination);
                if (copyResult.IsFailure)
                {
                    await OnOperationFailed();
                    return copyResult;
                }
            }
            else if (_resourceType == typeof(IFolderResource))
            {
                var copyResult = await CopyFolderInternal(SourceResourceKey, _resolvedDestination);
                if (copyResult.IsFailure)
                {
                    await OnOperationFailed();
                    return copyResult;
                }
            }
            else
            {
                await OnOperationFailed();
                return Result.Fail($"Unknown resource type for key: {SourceResourceKey}");
            }

            return Result.Ok();
        }

        public override async Task<Result> UndoAsync()
        {
            if (!_workspaceWrapper.IsWorkspacePageLoaded)
            {
                return Result.Fail($"Failed to undo copy resource. Workspace is not loaded");
            }

            // Reset the cached destination to clean up
            var resolvedDestination = _resolvedDestination;
            _resolvedDestination = new();

            if (Operation == CopyResourceOperation.Move)
            {
                // Preform undo by moving the resource back to its original location
                if (_resourceType == typeof(IFileResource))
                {
                    _resourceType = null;
                    var copyResult = await CopyFileInternal(resolvedDestination, SourceResourceKey);
                    if (copyResult.IsFailure)
                    {
                        await OnOperationFailed();
                        return copyResult;
                    }
                }
                else if (_resourceType == typeof(IFolderResource))
                {
                    _resourceType = null;
                    var copyResult = await CopyFolderInternal(resolvedDestination, SourceResourceKey);
                    if (copyResult.IsFailure)
                    {
                        await OnOperationFailed();
                        return copyResult;
                    }
                }
                else
                {
                    return Result.Fail($"Unknown resource type for key: {SourceResourceKey}");
                }
            }
            else if (Operation == CopyResourceOperation.Copy)
            {
                // Perform undo by deleting the previously copied resource.
                var deleteResult = await DeleteCopiedResource();
                if (deleteResult.IsFailure)
                {
                    return deleteResult;
                }
            }

            // Ensure the Tree View is synced with the files and folders on disk
            var message = new RequestResourceTreeUpdate();
            _messengerService.Send(message);

            return Result.Ok();
        }

        /// <summary>
        /// Resolves the destination resource key.
        /// If destResourceKey specifies an existing folder, then we append the name of the source resource 
        /// to the destination folder resource key. In all other situations we return the destResourceKey unchanged.
        /// </summary>
        private ResourceKey ResolveDestinationResourceKey(ResourceKey sourceResourceKey, ResourceKey destResourceKey)
        {
            string output = destResourceKey;

            var resourceRegistry = _workspaceWrapper.WorkspaceService.ProjectService.ResourceRegistry;
            var getResult = resourceRegistry.GetResource(destResourceKey);
            if (getResult.IsSuccess)
            {
                var resource = getResult.Value;
                if (resource is IFolderResource)
                {
                    if (destResourceKey.IsEmpty)
                    {
                        output = sourceResourceKey.ResourceName;
                    }
                    else
                    {
                        output = destResourceKey + "/" + sourceResourceKey.ResourceName;
                    }
                }
            }

            return output;
        }

        private async Task<Result> DeleteCopiedResource()
        {
            try
            {
                foreach (var filePath in _copiedFilePaths)
                {
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }
                }

                foreach (var folderPath in _copiedFolderPaths)
                {
                    if (Directory.Exists(folderPath))
                    {
                        Directory.Delete(folderPath, true);
                    }
                }
            }
            catch (Exception ex)
            {
                await OnOperationFailed();
                return Result.Fail($"Failed to delete copied resource. {ex.Message}");
            }
            finally
            {
                _copiedFilePaths.Clear();
                _copiedFolderPaths.Clear();
            }

            return Result.Ok();
        }

        private async Task<Result> CopyFileInternal(ResourceKey resourceKeyA, ResourceKey resourceKeyB)
        {
            if (resourceKeyA == resourceKeyB)
            {
                return Result.Fail($"Source and destination are the same: '{resourceKeyA}'");
            }

            var loadedProjectData = _projectDataService.LoadedProjectData;
            Guard.IsNotNull(loadedProjectData);

            if (resourceKeyA.IsEmpty || resourceKeyB.IsEmpty)
            {
                return Result.Fail("Resource key is empty.");
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
                    return Result.Fail($"File does not exist: {filePathA}");
                }

                if (File.Exists(filePathB))
                {
                    return Result.Fail($"File already exists: {filePathB}");
                }

                var parentFolderPathB = Path.GetDirectoryName(filePathB);
                if (!Directory.Exists(parentFolderPathB))
                {
                    return Result.Fail($"Target folder does not exist: {parentFolderPathB}");
                }

                if (Operation == CopyResourceOperation.Copy)
                {
                    File.Copy(filePathA, filePathB);

                    // Keep a note of the copied file so we can delete it in the undo
                    _copiedFilePaths.Add(filePathB);
                }
                else
                {
                    File.Move(filePathA, filePathB);                
                }

                var resourceRegistry = _workspaceWrapper.WorkspaceService.ProjectService.ResourceRegistry;
                var newParentFolder = resourceKeyB.GetParent();
                if (!newParentFolder.IsEmpty)
                {
                    resourceRegistry.SetFolderIsExpanded(newParentFolder, true);
                }
            }
            catch (Exception ex)
            {
                return Result.Fail($"Failed to copy file. {ex.Message}");
            }

            var message = new RequestResourceTreeUpdate();
            _messengerService.Send(message);

            await Task.CompletedTask;

            return Result.Ok();
        }

        private async Task<Result> CopyFolderInternal(ResourceKey resourceKeyA, ResourceKey resourceKeyB)
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
                return Result.Fail("Resource key is empty.");
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
                    return Result.Fail($"Folder path does not exist: {folderPathA}");
                }

                if (Directory.Exists(folderPathB))
                {
                    return Result.Fail($"Folder path already exists: {folderPathB}");
                }

                if (Operation == CopyResourceOperation.Copy)
                {
                    CopyFolder(folderPathA, folderPathB);

                    // Keep a note of the copied folder so we can delete it in the undo
                    _copiedFolderPaths.Add(folderPathB);
                }
                else
                {
                    Directory.Move(folderPathA, folderPathB);
                }

            }
            catch (Exception ex)
            {
                return Result.Fail($"Failed to copy folder. {ex.Message}");
            }

            var newParentFolder = resourceKeyB.GetParent();
            if (!newParentFolder.IsEmpty)
            {
                resourceRegistry.SetFolderIsExpanded(newParentFolder, true);
            }

            if (ExpandCopiedFolder)
            {
                resourceRegistry.SetFolderIsExpanded(resourceKeyB, true);
            }

            var message = new RequestResourceTreeUpdate();
            _messengerService.Send(message);

            await Task.CompletedTask;

            return Result.Ok();
        }

        private void CopyFolder(string sourceFolder, string destFolder)
        {
            DirectoryInfo dir = new DirectoryInfo(sourceFolder);

            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException($"Source directory does not exist or could not be found: {sourceFolder}");
            }

            DirectoryInfo[] dirs = dir.GetDirectories();
            if (!Directory.Exists(destFolder))
            {
                Directory.CreateDirectory(destFolder);
            }

            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                string tempPath = Path.Combine(destFolder, file.Name);
                file.CopyTo(tempPath);
            }

            foreach (DirectoryInfo subdir in dirs)
            {
                string tempPath = Path.Combine(destFolder, subdir.Name);
                CopyFolder(subdir.FullName, tempPath);
            }
        }

        private async Task OnOperationFailed()
        {
            var titleKey = Operation == CopyResourceOperation.Copy ? "ResourceTree_CopyResource" : "ResourceTree_MoveResource";
            var messageKey = Operation == CopyResourceOperation.Copy ? "ResourceTree_CopyResourceFailed" : "ResourceTree_MoveResourceFailed";

            var titleString = _stringLocalizer.GetString(titleKey);
            var messageString = _stringLocalizer.GetString(messageKey, SourceResourceKey, DestResourceKey);
            await _dialogService.ShowAlertDialogAsync(titleString, messageString);

            // Ensure the Tree View is synced with the files and folders on disk
            var message = new RequestResourceTreeUpdate();
            _messengerService.Send(message);
        }

        private static void CopyResourceInternal(ResourceKey sourceResourceKey, ResourceKey destResourceKey, CopyResourceOperation operation)
        {
            // Execute the copy resource command
            var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
            commandService.Execute<ICopyResourceCommand>(command =>
            {
                command.SourceResourceKey = sourceResourceKey;
                command.DestResourceKey = destResourceKey;
                command.Operation = operation;
            });
        }

        //
        // Static methods for scripting support.
        //

        public static void CopyResource(ResourceKey sourceResourceKey, ResourceKey destResourceKey)
        {
            CopyResourceInternal(sourceResourceKey, destResourceKey, CopyResourceOperation.Copy);
        }

        public static void MoveResource(ResourceKey sourceResourceKey, ResourceKey destResourceKey)
        {
            CopyResourceInternal(sourceResourceKey, destResourceKey, CopyResourceOperation.Move);
        }
    }
}
