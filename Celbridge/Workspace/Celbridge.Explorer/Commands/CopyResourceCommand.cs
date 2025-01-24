using Celbridge.Commands;
using Celbridge.DataTransfer;
using Celbridge.Dialog;
using Celbridge.Projects;
using Celbridge.Explorer.Models;
using Celbridge.Explorer.Services;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Localization;

namespace Celbridge.Explorer.Commands;

public class CopyResourceCommand : CommandBase, ICopyResourceCommand
{
    public override CommandFlags CommandFlags => CommandFlags.Undoable | CommandFlags.UpdateResources;

    public ResourceKey SourceResource { get; set; }
    public ResourceKey DestResource { get; set; }
    public DataTransferMode TransferMode { get; set; }
    public bool ExpandCopiedFolder { get; set; }

    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectService _projectService;
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;

    private Type? _resourceType;
    private ResourceKey _resolvedDestResource;
    private List<string> _copiedFilePaths = new();
    private List<string> _copiedFolderPaths = new();

    public CopyResourceCommand(
        IMessengerService messengerService,
        IWorkspaceWrapper workspaceWrapper,
        IProjectService projectService,
        IDialogService dialogService,
        IStringLocalizer stringLocalizer)
    {
        _messengerService = messengerService;
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

        var projectFolderPath = _projectService.CurrentProject!.ProjectFolderPath;
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

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;

        // Resolve references to folder resource
        _resolvedDestResource = resourceRegistry.ResolveDestinationResource(SourceResource, DestResource);

        if (_resourceType == typeof(IFileResource))
        {
            var copyResult = CopyFileResource(SourceResource, _resolvedDestResource);
            if (copyResult.IsFailure)
            {
                await OnOperationFailed();
                return copyResult;
            }
        }
        else if (_resourceType == typeof(IFolderResource))
        {
            var copyResult = CopyFolderResource(SourceResource, _resolvedDestResource);
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

        if (TransferMode == DataTransferMode.Move)
        {
            // Preform undo by moving the resource back to its original location
            if (_resourceType == typeof(IFileResource))
            {
                _resourceType = null;
                var copyResult = CopyFileResource(resolvedDestResource, SourceResource);
                if (copyResult.IsFailure)
                {
                    await OnOperationFailed();
                    return copyResult;
                }
            }
            else if (_resourceType == typeof(IFolderResource))
            {
                _resourceType = null;
                var copyResult = CopyFolderResource(resolvedDestResource, SourceResource);
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
        else if (TransferMode == DataTransferMode.Copy)
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
            return Result.Fail($"An exception occurred when deleting the copied resource.")
                .WithException(ex);
        }
        finally
        {
            _copiedFilePaths.Clear();
            _copiedFolderPaths.Clear();
        }

        return Result.Ok();
    }

    private Result CopyFileResource(ResourceKey resourceA, ResourceKey resourceB)
    {
        if (resourceA == resourceB)
        {
            return Result.Fail($"Source and destination are the same: '{resourceA}'");
        }

        var project = _projectService.CurrentProject;
        Guard.IsNotNull(project);

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;
        var entityService = _workspaceWrapper.WorkspaceService.EntityService;

        if (resourceA.IsEmpty || resourceB.IsEmpty)
        {
            return Result.Fail("Resource key is empty.");
        }

        try
        {
            var projectFolderPath = project.ProjectFolderPath;
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

            if (TransferMode == DataTransferMode.Copy)
            {
                // Copy the entity data file for the resource (if one exists).
                var copyEntityResult = entityService.CopyEntityDataFile(resourceA, resourceB);
                if (copyEntityResult.IsFailure)
                {
                    return Result.Fail($"Failed to copy entity data file for resource: {resourceA} to {resourceB}")
                        .WithErrors(copyEntityResult);
                }

                File.Copy(filePathA, filePathB);

                // Keep a note of the copied file so we can delete it if the command is undone.
                // Note that there's no need to track the copied entity file because it will be deleted automatically
                // when the resource registry is updated.
                _copiedFilePaths.Add(filePathB);
            }
            else
            {
                // Move the entity data file for the resource (if one exists).
                var moveEntityResult = entityService.MoveEntityDataFile(resourceA, resourceB);
                if (moveEntityResult.IsFailure)
                {
                    return Result.Fail($"Failed to move entity data file for resource: {resourceA} to {resourceB}")
                        .WithErrors(moveEntityResult);
                }

                File.Move(filePathA, filePathB);
            }

            var newParentFolder = resourceB.GetParent();
            if (!newParentFolder.IsEmpty)
            {
                resourceRegistry.SetFolderIsExpanded(newParentFolder, true);
            }
        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when copying the file.")
                .WithException(ex);
        }

        return Result.Ok();
    }

    private Result CopyFolderResource(ResourceKey resourceA, ResourceKey resourceB)
    {
        if (resourceA == resourceB)
        {
            return Result.Ok();
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ExplorerService.ResourceRegistry;
        var project = _projectService.CurrentProject;

        Guard.IsNotNull(project);

        if (resourceA.IsEmpty || resourceB.IsEmpty)
        {
            return Result.Fail("Resource key is empty.");
        }

        try
        {
            var projectFolderPath = project.ProjectFolderPath;
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

            if (TransferMode == DataTransferMode.Copy)
            {
                ResourceUtils.CopyFolder(folderPathA, folderPathB);

                // Keep a note of the copied folder so we can delete it in the undo
                _copiedFolderPaths.Add(folderPathB);
            }
            else
            {
                Directory.Move(folderPathA, folderPathB);

                // Notify opened documents that the resources in this folder have moved.
                SendFolderResourceKeyChangedMessages(resourceA, resourceB);
            }

        }
        catch (Exception ex)
        {
            return Result.Fail($"An exception occurred when copying the folder.")
                .WithException(ex);
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

        return Result.Ok();
    }

    private async Task OnOperationFailed()
    {
        var titleKey = TransferMode == DataTransferMode.Copy ? "ResourceTree_CopyResource" : "ResourceTree_MoveResource";
        var messageKey = TransferMode == DataTransferMode.Copy ? "ResourceTree_CopyResourceFailed" : "ResourceTree_MoveResourceFailed";

        var titleString = _stringLocalizer.GetString(titleKey);
        var messageString = _stringLocalizer.GetString(messageKey, SourceResource, DestResource);
        await _dialogService.ShowAlertDialogAsync(titleString, messageString);
    }

    private static void CopyResourceInternal(ResourceKey sourceResource, ResourceKey destResource, DataTransferMode operation)
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

    private void SendFolderResourceKeyChangedMessages(ResourceKey folderResourceA, ResourceKey folderResourceB)
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ExplorerService.ResourceRegistry;

        var messages = new List<ResourceKeyChangedMessage>();

        var getResourceResult = resourceRegistry.GetResource(folderResourceA);
        Guard.IsTrue(getResourceResult.IsSuccess);

        var sourceFolderA = getResourceResult.Value as FolderResource;
        Guard.IsNotNull(sourceFolderA);

        // Build a list of all the resources in the source folder (including the source folder itself)
        List<ResourceKey> sourceResources = new();
        PopulateSourceResources(sourceFolderA);

        void PopulateSourceResources(FolderResource folderResource)
        {
            var folderKey = resourceRegistry.GetResourceKey(folderResource);
            sourceResources.Add(folderKey);

            foreach (var childResource in folderResource.Children)
            {
                if (childResource is FolderResource childFolderResource)
                {
                    PopulateSourceResources(childFolderResource);
                }
                else
                {
                    var fileKey = resourceRegistry.GetResourceKey(childResource);
                    sourceResources.Add(fileKey);
                }
            }
        }

        // Send a message for every source resource that has moved
        foreach (var sourceResource in sourceResources)
        {
            // Generate the destination resource key and path
            var destResource = sourceResource.ToString().Replace(folderResourceA, folderResourceB);
            var destPath = resourceRegistry.GetResourcePath(destResource);

            var message = new ResourceKeyChangedMessage(sourceResource, destResource);
            _messengerService.Send(message);
        }
    }

    //
    // Static methods for scripting support.
    //

    public static void CopyResource(ResourceKey sourceResource, ResourceKey destResource)
    {
        CopyResourceInternal(sourceResource, destResource, DataTransferMode.Copy);
    }

    public static void MoveResource(ResourceKey sourceResource, ResourceKey destResource)
    {
        CopyResourceInternal(sourceResource, destResource, DataTransferMode.Move);
    }
}
