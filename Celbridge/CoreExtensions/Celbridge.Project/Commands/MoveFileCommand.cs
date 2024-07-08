using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Utilities;
using Celbridge.BaseLibrary.Workspace;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Localization;

namespace Celbridge.Project.Commands;

public class MoveFileCommand : CommandBase, IMoveFileCommand
{
    public override string StackName => CommandStackNames.Project;

    public string FromFilePath { get; set; } = string.Empty;
    public string ToFilePath { get; set; } = string.Empty;

    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectDataService _projectDataService;
    private readonly IUtilityService _utilityService;
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;

    public MoveFileCommand(
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
            return Result.Fail($"Failed to move file because workspace is not loaded");
        }

        var createResult = await MoveFileInternal(FromFilePath, ToFilePath);
        if (createResult.IsFailure)
        {
            var titleText = _stringLocalizer.GetString("ResourceTree_MoveFile");
            var bodyText = _stringLocalizer.GetString("ResourceTree_MoveFileFailed", FromFilePath, ToFilePath);

            // Show alert
            await _dialogService.ShowAlertDialogAsync(titleText, bodyText);
        }

        return createResult;
    }

    public override async Task<Result> UndoAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to move file because workspace is not loaded");
        }

        var createResult = await MoveFileInternal(ToFilePath, FromFilePath);
        if (createResult.IsFailure)
        {
            var titleText = _stringLocalizer.GetString("ResourceTree_MoveFile");
            var bodyText = _stringLocalizer.GetString("ResourceTree_MoveFileFailed", ToFilePath, FromFilePath);

            // Show alert
            await _dialogService.ShowAlertDialogAsync(titleText, bodyText);
        }

        return createResult;
    }

    /// <summary>
    /// Move the file resource at filePathA to filePathB.
    /// </summary>
    private async Task<Result> MoveFileInternal(string filePathA, string filePathB)
    {
        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ProjectService.ResourceRegistry;
        var loadedProjectData = _projectDataService.LoadedProjectData;

        Guard.IsNotNull(loadedProjectData);

        //
        // Validate the file paths
        //

        if (string.IsNullOrEmpty(filePathA) ||
            string.IsNullOrEmpty(filePathB))
        {
            return Result.Fail("Failed to move file. File path is empty.");
        }

        //
        // Move the file on disk
        //

        try
        {
            var projectFolder = loadedProjectData.ProjectFolder;
            var absoluteFilePathA = Path.Combine(projectFolder, filePathA);
            absoluteFilePathA = Path.GetFullPath(absoluteFilePathA); // Make separators consistent
            var absoluteFilePathB = Path.Combine(projectFolder, filePathB);
            absoluteFilePathB = Path.GetFullPath(absoluteFilePathB);

            if (!File.Exists(absoluteFilePathA))
            {
                return Result.Fail($"Failed to move file. File path does not exist: {filePathA}");
            }

            if (File.Exists(absoluteFilePathB))
            {
                return Result.Fail($"Failed to move file. File path already exists: {filePathB}");
            }

            File.Move(absoluteFilePathA, absoluteFilePathB);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to move file. {ex.Message}");
        }

        //
        // Update the resource registry to move the file resource
        //

        var updateResult = resourceRegistry.UpdateRegistry();
        if (updateResult.IsFailure)
        {
            return Result.Fail($"Failed to move file. {updateResult.Error}");
        }

        await Task.CompletedTask;

        return Result.Ok();
    }

    public static void MoveFile(string fromFilePath, string toFilePath)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IMoveFileCommand>(command =>
        {
            command.FromFilePath = fromFilePath;
            command.ToFilePath = toFilePath;
        });
    }
}
