using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.Utilities;
using Celbridge.BaseLibrary.Workspace;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Localization;

namespace Celbridge.Project.Commands;

public class AddFolderCommand : CommandBase, IAddFolderCommand
{
    public override string UndoStackName => UndoStackNames.Project;

    public ResourceKey ResourceKey { get; set; }

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
        var addResult = await AddFolderAsync();
        if (addResult.IsFailure)
        {
            var titleString = _stringLocalizer.GetString("ResourceTree_AddFolder");
            var messageString = _stringLocalizer.GetString("ResourceTree_FailedToAddFolder", ResourceKey);

            // Show alert
            await _dialogService.ShowAlertDialogAsync(titleString, messageString);
        }

        return addResult;
    }

    public override async Task<Result> UndoAsync()
    {
        var undoResult = await UndoAddFolderAsync();
        if (undoResult.IsFailure)
        {
            var titleString = _stringLocalizer.GetString("ResourceTree_AddFolder");
            var messageString = _stringLocalizer.GetString("ResourceTree_FailedToUndoAddFolder", ResourceKey);

            // Show alert
            await _dialogService.ShowAlertDialogAsync(titleString, messageString);
        }

        return undoResult;
    }

    private async Task<Result> AddFolderAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to add folder because workspace is not loaded");
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ProjectService.ResourceRegistry;
        var loadedProjectData = _projectDataService.LoadedProjectData;

        Guard.IsNotNull(loadedProjectData);

        //
        // Validate the resource key
        //

        if (ResourceKey.IsEmpty)
        {
            return Result.Fail("Failed to add folder. Resource key is empty");
        }

        if (!_utilityService.IsValidResourceKey(ResourceKey))
        {
            return Result.Fail($"Failed to add folder. Resource key '{ResourceKey}' is not valid.");
        }

        //
        // Add the folder on disk
        //

        try
        {
            var projectFolderPath = loadedProjectData.ProjectFolderPath;
            var newFolderPath = Path.Combine(projectFolderPath, ResourceKey);
            newFolderPath = Path.GetFullPath(newFolderPath); // Make separators consistent

            // It's important to fail if the folder already exists, because undoing this command
            // deletes the folder, which could lead to unexpected data loss.
            if (Directory.Exists(newFolderPath))
            {
                return Result.Fail($"Failed to add folder. A folder already exists at '{newFolderPath}'.");
            }

            Directory.CreateDirectory(newFolderPath);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to add folder. {ex.Message}");
        }

        //
        // Expand the folder containing the newly added folder
        //
        var parentResourceKey = ResourceKey.GetParent();
        if (!parentResourceKey.IsEmpty)
        {
            resourceRegistry.SetFolderIsExpanded(parentResourceKey, true);
        }

        var message = new RequestResourceTreeUpdate();
        _messengerService.Send(message);

        await Task.CompletedTask;

        return Result.Ok();
    }

    private async Task<Result> UndoAddFolderAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to undo add folder because workspace is not loaded");
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ProjectService.ResourceRegistry;
        var loadedProjectData = _projectDataService.LoadedProjectData;

        Guard.IsNotNull(loadedProjectData);

        //
        // Validate the resource key
        //

        if (ResourceKey.IsEmpty)
        {
            return Result.Fail("Failed to undo add folder. Resource key is empty");
        }

        // We can assume the resource key is valid because the command already executed successfully.

        //
        // Delete the folder on disk
        //

        try
        {
            var projectFolderPath = loadedProjectData.ProjectFolderPath;
            var newFolderPath = Path.Combine(projectFolderPath, ResourceKey);
            newFolderPath = Path.GetFullPath(newFolderPath); // Make separators consistent

            if (!Directory.Exists(newFolderPath))
            {
                return Result.Fail($"Failed to undo add folder. Folder does not exist '{newFolderPath}'.");
            }

            Directory.Delete(newFolderPath, true);
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

    public static void AddFolder(string resourceKey)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IAddFolderCommand>(command => command.ResourceKey = resourceKey);
    }
}
