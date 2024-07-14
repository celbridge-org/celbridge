using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.Utilities;
using Celbridge.BaseLibrary.Workspace;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Localization;
using Windows.Storage;

namespace Celbridge.Project.Commands;

public class AddFolderCommand : CommandBase, IAddFolderCommand
{
    public override string StackName => CommandStackNames.Project;

    public string ResourcePath { get; set; } = string.Empty;

    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectDataService _projectDataService;
    private readonly IUtilityService _utilityService;
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;

    public AddFolderCommand(
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
        var createResult = await CreateFolder();
        if (createResult.IsFailure)
        {
            var titleString = _stringLocalizer.GetString("ResourceTree_AddFolder");
            var messageString = _stringLocalizer.GetString("ResourceTree_FailedToCreateFolder", ResourcePath);

            // Show alert
            await _dialogService.ShowAlertDialogAsync(titleString, messageString);
        }

        return createResult;
    }

    private async Task<Result> CreateFolder()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to create folder because workspace is not loaded");
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ProjectService.ResourceRegistry;
        var loadedProjectData = _projectDataService.LoadedProjectData;

        Guard.IsNotNull(loadedProjectData);

        //
        // Validate the resource path
        //

        if (string.IsNullOrEmpty(ResourcePath))
        {
            return Result.Fail("Failed to add folder. Resource path is empty");
        }

        if (!_utilityService.IsValidResourcePath(ResourcePath))
        {
            return Result.Fail($"Failed to add folder. Resource path '{ResourcePath}' is not valid.");
        }

        //
        // Create the folder on disk
        //

        try
        {
            var newFolderPath = Path.GetFullPath(Path.Combine(loadedProjectData.ProjectFolder, ResourcePath));
            var parentFolderPath = Path.GetDirectoryName(newFolderPath);
            var newFolderName = Path.GetFileName(newFolderPath);

            if (parentFolderPath == null || newFolderName == null)
            {
                return Result.Fail("Failed to add folder. Invalid parent folder path or new folder name.");
            }

            var parentFolder = await StorageFolder.GetFolderFromPathAsync(parentFolderPath);

            // It's important to fail if the folder already exists, because undoing this command
            // deletes the folder, which could lead to unexpected data loss.

            var existingFolder = await parentFolder.TryGetItemAsync(newFolderName) as StorageFolder;
            if (existingFolder != null)
            {
                return Result.Fail($"Failed to add folder. A folder already exists at '{newFolderPath}'.");
            }

            await parentFolder.CreateFolderAsync(newFolderName, CreationCollisionOption.FailIfExists);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to add folder. {ex.Message}");
        }


        //
        // Expand the folder containing the newly created folder
        //
        int lastSlashIndex = ResourcePath.LastIndexOf('/');
        if (lastSlashIndex > -1)
        {
            var parentFolder = ResourcePath.Substring(0, lastSlashIndex);
            if (!string.IsNullOrEmpty(parentFolder))
            {
                resourceRegistry.SetFolderIsExpanded(parentFolder, true);
            }
        }

        var message = new RequestResourceTreeUpdate();
        _messengerService.Send(message);

        await Task.CompletedTask;

        return Result.Ok();
    }

    public override async Task<Result> UndoAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to undo create folder because workspace is not loaded");
        }

        var loadedProjectData = _projectDataService.LoadedProjectData;

        Guard.IsNotNull(loadedProjectData);

        //
        // Validate the resource path
        //

        if (string.IsNullOrEmpty(ResourcePath))
        {
            return Result.Fail("Failed to undo add folder. Resource path is empty");
        }

        // We can assume the resource path is valid because the command already executed successfully.

        //
        // Delete the folder on disk
        //

        try
        {
            var newFolderPath = Path.GetFullPath(Path.Combine(loadedProjectData.ProjectFolder, ResourcePath));
            var parentFolderPath = Path.GetDirectoryName(newFolderPath);
            var folderName = Path.GetFileName(newFolderPath);

            if (parentFolderPath == null || folderName == null)
            {
                return Result.Fail("Failed to undo add folder. Invalid parent folder path or folder name.");
            }

            var parentFolder = await StorageFolder.GetFolderFromPathAsync(parentFolderPath);

            var folderToDelete = await parentFolder.TryGetItemAsync(folderName) as StorageFolder;
            if (folderToDelete == null)
            {
                return Result.Fail($"Failed to undo add folder. Folder does not exist '{newFolderPath}'.");
            }

            await folderToDelete.DeleteAsync(StorageDeleteOption.PermanentDelete);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to undo add folder. {ex.Message}");
        }

        var message = new RequestResourceTreeUpdate();
        _messengerService.Send(message);

        await Task.CompletedTask;

        return Result.Ok();
    }

    public static void AddFolder(string resourcePath)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IAddFolderCommand>(command => command.ResourcePath = resourcePath);
    }
}
