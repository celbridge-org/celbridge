using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Projects;
using Celbridge.Resources.Services;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Localization;

namespace Celbridge.Resources.Commands
{
    public class CopyResourceCommand : CommandBase, ICopyResourceCommand
    {
        public override string UndoStackName => UndoStackNames.Project;
        public override CommandFlags CommandFlags => CommandFlags.UpdateResources;

        public ResourceKey SourceResource { get; set; }
        public ResourceKey DestResource { get; set; }
        public ResourceTransferMode TransferMode { get; set; }
        public bool ExpandCopiedFolder { get; set; }

        private readonly IWorkspaceWrapper _workspaceWrapper;
        private readonly IProjectService _projectService;
        private readonly IDialogService _dialogService;
        private readonly IStringLocalizer _stringLocalizer;

        private Type? _resourceType;
        private ResourceKey _resolvedDestResource;
        private List<string> _copiedFilePaths = new();
        private List<string> _copiedFolderPaths = new();

        public CopyResourceCommand(
            IWorkspaceWrapper workspaceWrapper,
            IProjectService projectService,
            IDialogService dialogService,
            IStringLocalizer stringLocalizer)
        {
            _workspaceWrapper = workspaceWrapper;
            _projectService = projectService;
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

            var projectFolderPath = _projectService.LoadedProject!.ProjectFolderPath;
            if (string.IsNullOrEmpty(projectFolderPath))
            {
                return Result.Fail("Project folder path is empty.");
            }

            var resourcePath = Path.GetFullPath(Path.Combine(projectFolderPath, SourceResource));
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

            var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.ResourceRegistry;

            // Resolve references to folder resource
            _resolvedDestResource = resourceRegistry.ResolveDestinationResource(SourceResource, DestResource);

            if (_resourceType == typeof(IFileResource))
            {
                var copyResult = await CopyFileInternal(SourceResource, _resolvedDestResource);
                if (copyResult.IsFailure)
                {
                    await OnOperationFailed();
                    return copyResult;
                }
            }
            else if (_resourceType == typeof(IFolderResource))
            {
                var copyResult = await CopyFolderInternal(SourceResource, _resolvedDestResource);
                if (copyResult.IsFailure)
                {
                    await OnOperationFailed();
                    return copyResult;
                }
            }
            else
            {
                await OnOperationFailed();
                return Result.Fail($"Unknown resource type for key: {SourceResource}");
            }

            return Result.Ok();
        }

        public override async Task<Result> UndoAsync()
        {
            if (!_workspaceWrapper.IsWorkspacePageLoaded)
            {
                return Result.Fail($"Failed to undo copy resource. Workspace is not loaded");
            }

            // Clear the cached destination to clean up
            var resolvedDestResource = _resolvedDestResource;
            _resolvedDestResource = new();

            if (TransferMode == ResourceTransferMode.Move)
            {
                // Preform undo by moving the resource back to its original location
                if (_resourceType == typeof(IFileResource))
                {
                    _resourceType = null;
                    var copyResult = await CopyFileInternal(resolvedDestResource, SourceResource);
                    if (copyResult.IsFailure)
                    {
                        await OnOperationFailed();
                        return copyResult;
                    }
                }
                else if (_resourceType == typeof(IFolderResource))
                {
                    _resourceType = null;
                    var copyResult = await CopyFolderInternal(resolvedDestResource, SourceResource);
                    if (copyResult.IsFailure)
                    {
                        await OnOperationFailed();
                        return copyResult;
                    }
                }
                else
                {
                    return Result.Fail($"Unknown resource type for key: {SourceResource}");
                }
            }
            else if (TransferMode == ResourceTransferMode.Copy)
            {
                // Perform undo by deleting the previously copied resource.
                var deleteResult = await DeleteCopiedResource();
                if (deleteResult.IsFailure)
                {
                    return deleteResult;
                }
            }

            return Result.Ok();
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

        private async Task<Result> CopyFileInternal(ResourceKey resourceA, ResourceKey resourceB)
        {
            if (resourceA == resourceB)
            {
                return Result.Fail($"Source and destination are the same: '{resourceA}'");
            }

            var loadedProject = _projectService.LoadedProject;
            Guard.IsNotNull(loadedProject);

            if (resourceA.IsEmpty || resourceB.IsEmpty)
            {
                return Result.Fail("Resource key is empty.");
            }

            try
            {
                var projectFolderPath = loadedProject.ProjectFolderPath;
                var filePathA = Path.Combine(projectFolderPath, resourceA);
                filePathA = Path.GetFullPath(filePathA);
                var filePathB = Path.Combine(projectFolderPath, resourceB);
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

                if (TransferMode == ResourceTransferMode.Copy)
                {
                    File.Copy(filePathA, filePathB);

                    // Keep a note of the copied file so we can delete it in the undo
                    _copiedFilePaths.Add(filePathB);
                }
                else
                {
                    File.Move(filePathA, filePathB);                
                }

                var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.ResourceRegistry;
                var newParentFolder = resourceB.GetParent();
                if (!newParentFolder.IsEmpty)
                {
                    resourceRegistry.SetFolderIsExpanded(newParentFolder, true);
                }
            }
            catch (Exception ex)
            {
                return Result.Fail($"Failed to copy file. {ex.Message}");
            }

            await Task.CompletedTask;

            return Result.Ok();
        }

        private async Task<Result> CopyFolderInternal(ResourceKey resourceA, ResourceKey resourceB)
        {
            if (resourceA == resourceB)
            {
                return Result.Ok();
            }

            var workspaceService = _workspaceWrapper.WorkspaceService;
            var resourceRegistry = workspaceService.ResourceService.ResourceRegistry;
            var loadedProject = _projectService.LoadedProject;

            Guard.IsNotNull(loadedProject);

            if (resourceA.IsEmpty || resourceB.IsEmpty)
            {
                return Result.Fail("Resource key is empty.");
            }

            try
            {
                var projectFolderPath = loadedProject.ProjectFolderPath;
                var folderPathA = Path.Combine(projectFolderPath, resourceA);
                folderPathA = Path.GetFullPath(folderPathA);
                var folderPathB = Path.Combine(projectFolderPath, resourceB);
                folderPathB = Path.GetFullPath(folderPathB);

                if (!Directory.Exists(folderPathA))
                {
                    return Result.Fail($"Folder path does not exist: {folderPathA}");
                }

                if (Directory.Exists(folderPathB))
                {
                    return Result.Fail($"Folder path already exists: {folderPathB}");
                }

                if (TransferMode == ResourceTransferMode.Copy)
                {
                    ResourceUtils.CopyFolder(folderPathA, folderPathB);

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

            var newParentFolder = resourceB.GetParent();
            if (!newParentFolder.IsEmpty)
            {
                resourceRegistry.SetFolderIsExpanded(newParentFolder, true);
            }

            if (ExpandCopiedFolder)
            {
                resourceRegistry.SetFolderIsExpanded(resourceB, true);
            }

            await Task.CompletedTask;

            return Result.Ok();
        }

        private async Task OnOperationFailed()
        {
            var titleKey = TransferMode == ResourceTransferMode.Copy ? "ResourceTree_CopyResource" : "ResourceTree_MoveResource";
            var messageKey = TransferMode == ResourceTransferMode.Copy ? "ResourceTree_CopyResourceFailed" : "ResourceTree_MoveResourceFailed";

            var titleString = _stringLocalizer.GetString(titleKey);
            var messageString = _stringLocalizer.GetString(messageKey, SourceResource, DestResource);
            await _dialogService.ShowAlertDialogAsync(titleString, messageString);
        }

        private static void CopyResourceInternal(ResourceKey sourceResource, ResourceKey destResource, ResourceTransferMode operation)
        {
            // Execute the copy resource command
            var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
            commandService.Execute<ICopyResourceCommand>(command =>
            {
                command.SourceResource = sourceResource;
                command.DestResource = destResource;
                command.TransferMode = operation;
            });
        }

        //
        // Static methods for scripting support.
        //

        public static void CopyResource(ResourceKey sourceResource, ResourceKey destResource)
        {
            CopyResourceInternal(sourceResource, destResource, ResourceTransferMode.Copy);
        }

        public static void MoveResource(ResourceKey sourceResource, ResourceKey destResource)
        {
            CopyResourceInternal(sourceResource, destResource, ResourceTransferMode.Move);
        }
    }
}
