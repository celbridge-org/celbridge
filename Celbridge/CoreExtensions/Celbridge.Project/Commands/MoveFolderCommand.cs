using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.Workspace;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Localization;

namespace Celbridge.Project.Commands;

public class MoveFolderCommand : CommandBase, IMoveFolderCommand
{
    public override string UndoStackName => UndoStackNames.Project;

    public ResourceKey FromResourceKey { get; set; } = new();
    public ResourceKey ToResourceKey { get; set; } = new();
    public bool ExpandMovedFolder { get; set; } = false;

    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectDataService _projectDataService;
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;

    public MoveFolderCommand(
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
            return Result.Fail($"Failed to move folder. Workspace is not loaded");
        }

        var moveResult = await MoveFolderInternal(FromResourceKey, ToResourceKey);
        if (moveResult.IsFailure)
        {
            var titleString = _stringLocalizer.GetString("ResourceTree_MoveFolder");
            var messageString = _stringLocalizer.GetString("ResourceTree_MoveFolderFailed", FromResourceKey, ToResourceKey);

            // Show alert
            await _dialogService.ShowAlertDialogAsync(titleString, messageString);

            // The TreeView UI may now be out of sync with the actual project folder structure, so force a refresh.
            var message = new RequestResourceTreeUpdate();
            _messengerService.Send(message);
        }

        return moveResult;
    }

    public override async Task<Result> UndoAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to undo move folder. Workspace is not loaded");
        }

        var moveResult = await MoveFolderInternal(ToResourceKey, FromResourceKey);
        if (moveResult.IsFailure)
        {
            var titleString = _stringLocalizer.GetString("ResourceTree_MoveFolder");
            var messageString = _stringLocalizer.GetString("ResourceTree_MoveFolderFailed", ToResourceKey, FromResourceKey);

            // Show alert
            await _dialogService.ShowAlertDialogAsync(titleString, messageString);

            // The TreeView UI may now be out of sync with the actual project folder structure, so force a refresh.
            var message = new RequestResourceTreeUpdate();
            _messengerService.Send(message);
        }

        return moveResult;
    }

    /// <summary>
    /// Move the folder at resourceKeyA to resourceKeyB.
    /// </summary>
    private async Task<Result> MoveFolderInternal(ResourceKey resourceKeyA, ResourceKey resourceKeyB)
    {
        if (resourceKeyA == resourceKeyB)
        {
            // Moving to the same resource key is a no-op
            return Result.Ok();
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ProjectService.ResourceRegistry;
        var loadedProjectData = _projectDataService.LoadedProjectData;

        Guard.IsNotNull(loadedProjectData);

        //
        // Validate the folder resource keys
        //

        if (resourceKeyA.IsEmpty ||
            resourceKeyB.IsEmpty)
        {
            return Result.Fail("Failed to move folder. Resource key is empty.");
        }

        //
        // Move the folder on disk
        //

        try
        {
            var projectFolderPath = loadedProjectData.ProjectFolderPath;
            var folderPathA = Path.Combine(projectFolderPath, resourceKeyA);
            folderPathA = Path.GetFullPath(folderPathA); // Make separators consistent
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

        // Expand the parent folder of the moved folder so that the user can see it in the tree view.
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

    public static void MoveFolder(string fromResourceKey, string toResourceKey)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IMoveFolderCommand>(command =>
        {
            command.FromResourceKey = fromResourceKey;
            command.ToResourceKey = toResourceKey;
            command.ExpandMovedFolder = true;
        });
    }
}
