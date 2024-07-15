using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.Utilities;
using Celbridge.BaseLibrary.Workspace;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Localization;

namespace Celbridge.Project.Commands;

public class MoveFolderCommand : CommandBase, IMoveFolderCommand
{
    public override string StackName => CommandStackNames.Project;

    public string FromResourcePath { get; set; } = string.Empty;
    public string ToResourcePath { get; set; } = string.Empty;

    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectDataService _projectDataService;
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;

    public MoveFolderCommand(
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
        _dialogService = dialogService;
        _stringLocalizer = stringLocalizer;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to move folder. Workspace is not loaded");
        }

        var createResult = await MoveFolderInternal(FromResourcePath, ToResourcePath);
        if (createResult.IsFailure)
        {
            var titleString = _stringLocalizer.GetString("ResourceTree_MoveFolder");
            var messageString = _stringLocalizer.GetString("ResourceTree_MoveFolderFailed", FromResourcePath, ToResourcePath);

            // Show alert
            await _dialogService.ShowAlertDialogAsync(titleString, messageString);
        }

        return createResult;
    }

    public override async Task<Result> UndoAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to undo move folder. Workspace is not loaded");
        }

        var createResult = await MoveFolderInternal(ToResourcePath, FromResourcePath);
        if (createResult.IsFailure)
        {
            var titleString = _stringLocalizer.GetString("ResourceTree_MoveFolder");
            var messageString = _stringLocalizer.GetString("ResourceTree_MoveFolderFailed", ToResourcePath, FromResourcePath);

            // Show alert
            await _dialogService.ShowAlertDialogAsync(titleString, messageString);
        }

        return createResult;
    }

    /// <summary>
    /// Move the folder at resourcePathA to resourcePathB.
    /// </summary>
    private async Task<Result> MoveFolderInternal(string resourcePathA, string resourcePathB)
    {
        if (resourcePathA == resourcePathB)
        {
            // Moving to the same resource path is a no-op
            return Result.Ok();
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ProjectService.ResourceRegistry;
        var loadedProjectData = _projectDataService.LoadedProjectData;

        Guard.IsNotNull(loadedProjectData);

        //
        // Validate the folder paths
        //

        if (string.IsNullOrEmpty(resourcePathA) ||
            string.IsNullOrEmpty(resourcePathB))
        {
            return Result.Fail("Failed to move folder. Resource path is empty.");
        }

        var isFolderAExpanded = resourceRegistry.IsFolderExpanded(resourcePathA);

        //
        // Move the folder on disk
        //

        try
        {
            var projectFolderPath = loadedProjectData.ProjectFolderPath;
            var folderPathA = Path.Combine(projectFolderPath, resourcePathA);
            folderPathA = Path.GetFullPath(folderPathA); // Make separators consistent
            var folderPathB = Path.Combine(projectFolderPath, resourcePathB);
            folderPathB = Path.GetFullPath(folderPathB);

            if (!Directory.Exists(folderPathA))
            {
                return Result.Fail($"Failed to move folder. Folder path does not exist: {resourcePathA}");
            }

            if (Directory.Exists(folderPathB))
            {
                return Result.Fail($"Failed to move folder. Folder path already exists: {resourcePathB}");
            }

            Directory.Move(folderPathA, folderPathB);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to move folder. {ex.Message}");
        }

        resourceRegistry.SetFolderIsExpanded(resourcePathB, isFolderAExpanded);

        var message = new RequestResourceTreeUpdate();
        _messengerService.Send(message);

        await Task.CompletedTask;

        return Result.Ok();
    }

    public static void MoveFolder(string fromResourcePath, string toResourcePath)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IMoveFolderCommand>(command =>
        {
            command.FromResourcePath = fromResourcePath;
            command.ToResourcePath = toResourcePath;
        });
    }
}
