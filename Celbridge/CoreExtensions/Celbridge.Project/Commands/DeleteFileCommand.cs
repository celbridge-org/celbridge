using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Utilities;
using Celbridge.BaseLibrary.Workspace;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Localization;

namespace Celbridge.Project.Commands;

public class DeleteFileCommand : CommandBase, IDeleteFileCommand
{
    public override string StackName => CommandStackNames.None;

    public string FilePath { get; set; } = string.Empty;

    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectDataService _projectDataService;
    private readonly IUtilityService _utilityService;
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;

    public DeleteFileCommand(
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
        var createResult = await DeleteFile();
        if (createResult.IsFailure)
        {
            var titleText = _stringLocalizer.GetString("ResourceTree_DeleteFile");
            var bodyText = _stringLocalizer.GetString("ResourceTree_FailedToDeleteFile", FilePath);

            // Show alert
            await _dialogService.ShowAlertDialogAsync(titleText, bodyText);
        }

        return createResult;
    }

    private async Task<Result> DeleteFile()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to delete file because workspace is not loaded");
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ProjectService.ResourceRegistry;
        var loadedProjectData = _projectDataService.LoadedProjectData;

        Guard.IsNotNull(loadedProjectData);

        //
        // Validate the file path
        //

        if (string.IsNullOrEmpty(FilePath))
        {
            return Result.Fail("Failed to delete file. File path is empty");
        }

        //
        // Delete the file on disk
        //

        try
        {
            var projectFolder = loadedProjectData.ProjectFolder;
            var deleteFilePath = Path.Combine(projectFolder, FilePath);
            deleteFilePath = Path.GetFullPath(deleteFilePath); // Make separators consistent

            if (!File.Exists(deleteFilePath))
            {
                return Result.Fail($"Failed to delete file. File does not exist: {deleteFilePath}");
            }

            File.Delete(deleteFilePath);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to delete file. {ex.Message}");
        }

        //
        // Update the resource registry to remove the deleted file
        //

        var updateResult = resourceRegistry.UpdateRegistry();
        if (updateResult.IsFailure)
        {
            return Result.Fail($"Failed to delete file. {updateResult.Error}");
        }

        await Task.CompletedTask;

        return Result.Ok();
    }

    public static void DeleteFile(string filePath)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IDeleteFileCommand>(command => command.FilePath = filePath);
    }
}
