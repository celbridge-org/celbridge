using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.Project;
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
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;

    public MoveFileCommand(
        IWorkspaceWrapper workspaceWrapper,
        IProjectDataService projectDataService,
        IDialogService dialogService,
        IStringLocalizer stringLocalizer)
    {
        _workspaceWrapper = workspaceWrapper;
        _projectDataService = projectDataService;
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

            var parentFolderB = Path.GetDirectoryName(absoluteFilePathB);
            if (!Directory.Exists(parentFolderB))
            {
                // The parent folder of the target file path must exist before moving the file.
                // It would be easy to create the missing parent folder(s) here automatically, but it's hard to
                // undo this operation robustly.
                return Result.Fail($"Failed to move file. Target folder does not exist: {parentFolderB}");
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
        if (Path.GetExtension(fromFilePath) != Path.GetExtension(toFilePath))
        {
            // If the "from" and "to" extensions don't match then we assume that the user intended to move the
            // file to a folder, rather than moving/renaming to a file with no extension.
            // This means that this command cannot be used to remove the file extension from a file.
            toFilePath = Path.Combine(toFilePath, Path.GetFileName(fromFilePath));
        }

        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IMoveFileCommand>(command =>
        {
            command.FromFilePath = fromFilePath;
            command.ToFilePath = toFilePath;
        });
    }
}
