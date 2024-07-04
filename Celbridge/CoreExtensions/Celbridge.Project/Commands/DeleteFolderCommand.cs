using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Utilities;
using Celbridge.BaseLibrary.Workspace;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Localization;

namespace Celbridge.Project.Commands;

public class DeleteFolderCommand : CommandBase, IDeleteFolderCommand
{
    public override string StackName => CommandStackNames.None;

    public string FolderPath { get; set; } = string.Empty;

    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectDataService _projectDataService;
    private readonly IUtilityService _utilityService;
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;

    public DeleteFolderCommand(
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
        var createResult = await DeleteFolder();
        if (createResult.IsFailure)
        {
            var titleText = _stringLocalizer.GetString("ResourceTree_DeleteFolder");
            var bodyText = _stringLocalizer.GetString("ResourceTree_FailedToDeleteFolder", FolderPath);

            // Show alert
            await _dialogService.ShowAlertDialogAsync(titleText, bodyText);
        }

        return createResult;
    }

    private async Task<Result> DeleteFolder()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to delete folder because workspace is not loaded");
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ProjectService.ResourceRegistry;
        var loadedProjectData = _projectDataService.LoadedProjectData;

        Guard.IsNotNull(loadedProjectData);

        //
        // Validate the folder path
        //

        if (string.IsNullOrEmpty(FolderPath))
        {
            return Result.Fail("Failed to delete folder. Folder path is empty");
        }

        //
        // Delete the folder on disk
        //

        try
        {
            var projectFolder = loadedProjectData.ProjectFolder;
            var deleteFolderPath = Path.Combine(projectFolder, FolderPath);
            deleteFolderPath = Path.GetFullPath(deleteFolderPath); // Make separators consistent

            if (!Directory.Exists(deleteFolderPath))
            {
                return Result.Fail($"Failed to delete folder. Folder does not exist: {deleteFolderPath}");
            }

            Directory.Delete(deleteFolderPath, true);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to delete folder. {ex.Message}");
        }

        //
        // Update the resource registry to remove the deleted folder
        //

        var updateResult = resourceRegistry.UpdateRegistry();
        if (updateResult.IsFailure)
        {
            return Result.Fail($"Failed to delete resource. {updateResult.Error}");
        }

        await Task.CompletedTask;

        return Result.Ok();
    }

    public static void DeleteFolder(string folderPath)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IDeleteFolderCommand>(command => command.FolderPath = folderPath);
    }
}
