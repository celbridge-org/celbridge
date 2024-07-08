using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Utilities;
using Celbridge.BaseLibrary.Workspace;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Localization;

namespace Celbridge.Project.Commands;

public class MoveFolderCommand : CommandBase, IMoveFolderCommand
{
    public override string StackName => CommandStackNames.Project;

    public string FromFolderPath { get; set; } = string.Empty;
    public string ToFolderPath { get; set; } = string.Empty;

    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectDataService _projectDataService;
    private readonly IUtilityService _utilityService;
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;

    public MoveFolderCommand(
        IWorkspaceWrapper workspaceWrapper,
        IProjectDataService projectDataService,
        IUtilityService utilityService,
        IDialogService dialogService,
        IStringLocalizer stringLocalizer)
    {
        _workspaceWrapper = workspaceWrapper;
        _projectDataService = projectDataService;
        _utilityService = utilityService;
        _dialogService = dialogService;
        _stringLocalizer = stringLocalizer;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to move folder because workspace is not loaded");
        }

        var createResult = await MoveFolderInternal(FromFolderPath, ToFolderPath);
        if (createResult.IsFailure)
        {
            var titleText = _stringLocalizer.GetString("ResourceTree_MoveFolder");
            var bodyText = _stringLocalizer.GetString("ResourceTree_MoveFolderFailed", FromFolderPath, ToFolderPath);

            // Show alert
            await _dialogService.ShowAlertDialogAsync(titleText, bodyText);
        }

        return createResult;
    }

    public override async Task<Result> UndoAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to move folder because workspace is not loaded");
        }

        var createResult = await MoveFolderInternal(ToFolderPath, FromFolderPath);
        if (createResult.IsFailure)
        {
            var titleText = _stringLocalizer.GetString("ResourceTree_MoveFolder");
            var bodyText = _stringLocalizer.GetString("ResourceTree_MoveFolderFailed", ToFolderPath, FromFolderPath);

            // Show alert
            await _dialogService.ShowAlertDialogAsync(titleText, bodyText);
        }

        return createResult;
    }

    /// <summary>
    /// Move the folder resource at folderPathA to folderPathB.
    /// </summary>
    private async Task<Result> MoveFolderInternal(string folderPathA, string folderPathB)
    {
        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ProjectService.ResourceRegistry;
        var loadedProjectData = _projectDataService.LoadedProjectData;

        Guard.IsNotNull(loadedProjectData);

        //
        // Validate the folder paths
        //

        if (string.IsNullOrEmpty(folderPathA) ||
            string.IsNullOrEmpty(folderPathB))
        {
            return Result.Fail("Failed to move folder. Folder path is empty.");
        }

        var isFolderAExpanded = resourceRegistry.IsFolderExpanded(folderPathA);

        //
        // Move the folder on disk
        //

        try
        {
            var projectFolder = loadedProjectData.ProjectFolder;
            var absoluteFolderPathA = Path.Combine(projectFolder, folderPathA);
            absoluteFolderPathA = Path.GetFullPath(absoluteFolderPathA); // Make separators consistent
            var absoluteFolderPathB = Path.Combine(projectFolder, folderPathB);
            absoluteFolderPathB = Path.GetFullPath(absoluteFolderPathB);

            if (!Directory.Exists(absoluteFolderPathA))
            {
                return Result.Fail($"Failed to move folder. Folder path does not exist: {folderPathA}");
            }

            if (Directory.Exists(absoluteFolderPathB))
            {
                return Result.Fail($"Failed to move folder. Folder path already exists: {folderPathB}");
            }

            Directory.Move(absoluteFolderPathA, absoluteFolderPathB);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to move folder. {ex.Message}");
        }

        //
        // Update the resource registry to move the folder resource
        //

        var updateResult = resourceRegistry.UpdateRegistry();
        if (updateResult.IsFailure)
        {
            return Result.Fail($"Failed to move folder. {updateResult.Error}");
        }

        if (isFolderAExpanded)
        {
            resourceRegistry.SetExpandedFolders(new List<string>() { folderPathB });
        }

        await Task.CompletedTask;

        return Result.Ok();
    }

    public static void MoveFolder(string fromFolderPath, string toFolderPath)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IMoveFolderCommand>(command =>
        {
            command.FromFolderPath = fromFolderPath;
            command.ToFolderPath = toFolderPath;
        });
    }
}
