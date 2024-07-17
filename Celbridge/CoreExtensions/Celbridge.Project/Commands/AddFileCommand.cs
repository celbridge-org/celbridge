using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.Utilities;
using Celbridge.BaseLibrary.Workspace;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Localization;

namespace Celbridge.Project.Commands;

public class AddFileCommand : CommandBase, IAddFileCommand
{
    public override string UndoStackName => UndoStackNames.Project;

    public ResourceKey ResourceKey { get; set; }

    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectDataService _projectDataService;
    private readonly IUtilityService _utilityService;
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;

    public AddFileCommand(
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
        var addResult = await AddFileAsync();
        if (addResult.IsFailure)
        {
            var titleString = _stringLocalizer.GetString("ResourceTree_AddFile");
            var messageString = _stringLocalizer.GetString("ResourceTree_AddFileFailed", ResourceKey);

            // Show alert
            await _dialogService.ShowAlertDialogAsync(titleString, messageString);
        }

        return addResult;
    }

    public override async Task<Result> UndoAsync()
    {
        var undoResult = await UndoAddFileAsync();
        if (undoResult.IsFailure)
        {
            var titleString = _stringLocalizer.GetString("ResourceTree_AddFile");
            var messageString = _stringLocalizer.GetString("ResourceTree_UndoAddFileFailed", ResourceKey);

            // Show alert
            await _dialogService.ShowAlertDialogAsync(titleString, messageString);
        }

        return undoResult;

    }

    private async Task<Result> AddFileAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to create file because workspace is not loaded");
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
            return Result.Fail("Failed to create file. Resource key is empty");
        }

        if (!_utilityService.IsValidResourceKey(ResourceKey))
        {
            return Result.Fail($"Failed to create file. Resource key '{ResourceKey}' is not valid.");
        }

        //
        // Create the file on disk
        //

        try
        {
            var projectFolderPath = loadedProjectData.ProjectFolderPath;
            var newFilePath = Path.Combine(projectFolderPath, ResourceKey);
            newFilePath = Path.GetFullPath(newFilePath); // Make separators consistent

            // It's important to fail if the file already exists, because undoing this command
            // deletes the file, which could lead to unexpected data loss.
            if (File.Exists(newFilePath))
            {
                return Result.Fail($"Failed to create file. A file already exists at '{newFilePath}'.");
            }

            File.WriteAllText(newFilePath, string.Empty);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to create file. {ex.Message}");
        }

        //
        // Expand the folder containing the newly created file
        //
        var parentFolderKey = ResourceKey.GetParent();
        if (!parentFolderKey.IsEmpty)
        {
            resourceRegistry.SetFolderIsExpanded(parentFolderKey, true);
        }

        var message = new RequestResourceTreeUpdate();
        _messengerService.Send(message);

        await Task.CompletedTask;

        return Result.Ok();
    }

    private async Task<Result> UndoAddFileAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to undo add file. Workspace is not loaded");
        }

        var loadedProjectData = _projectDataService.LoadedProjectData;

        Guard.IsNotNull(loadedProjectData);

        //
        // Validate the resource key
        //

        if (ResourceKey.IsEmpty)
        {
            return Result.Fail("Failed to undo add file. Resource key is empty");
        }

        // We can assume the resource key is valid because the command already executed successfully.

        //
        // Delete the file on disk
        //

        try
        {
            var projectFolderPath = loadedProjectData.ProjectFolderPath;
            var newFilePath = Path.Combine(projectFolderPath, ResourceKey);
            newFilePath = Path.GetFullPath(newFilePath); // Make separators consistent

            if (!File.Exists(newFilePath))
            {
                return Result.Fail($"Failed to undo add file. File does not exist: {newFilePath}");
            }

            File.Delete(newFilePath);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to undo add file. {ex.Message}");
        }

        var message = new RequestResourceTreeUpdate();
        _messengerService.Send(message);

        await Task.CompletedTask;

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void AddFile(ResourceKey resourceKey)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IAddFileCommand>(command => command.ResourceKey = resourceKey);
    }
}
