using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Utilities;
using Celbridge.BaseLibrary.Workspace;
using Celbridge.Utilities.Services;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Localization;
using System.IO.Compression;

namespace Celbridge.Project.Commands;

public class DeleteFolderCommand : CommandBase, IDeleteFolderCommand
{
    public override string StackName => CommandStackNames.Project;

    public string FolderPath { get; set; } = string.Empty;

    private string _archivePath = string.Empty;
    private bool _folderWasExpanded;

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
            var titleString = _stringLocalizer.GetString("ResourceTree_DeleteFolder");
            var messageString = _stringLocalizer.GetString("ResourceTree_FailedToDeleteFolder", FolderPath);

            // Show alert
            await _dialogService.ShowAlertDialogAsync(titleString, messageString);
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

            // Generate a random file name for the archive
            _archivePath = _utilityService.GetTemporaryFilePath(PathConstants.DeletedFilesFolder, ".zip");
            if (File.Exists(_archivePath))
            {
                File.Delete(_archivePath);
            }

            var archiveFolder = Path.GetDirectoryName(_archivePath)!;
            Directory.CreateDirectory(archiveFolder);

            // Archive the folder to temporary storage so we can undo the command
            ZipFile.CreateFromDirectory(deleteFolderPath, _archivePath, CompressionLevel.Optimal, includeBaseDirectory: false);

            // Record if the folder was expanded so we can expand it again in the undo if needed
            _folderWasExpanded = resourceRegistry.IsFolderExpanded(FolderPath);

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

    public override async Task<Result> UndoAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to undo delete folder because workspace is not loaded");
        }

        if (!File.Exists(_archivePath))
        {
            return Result.Fail($"Failed to undo folder delete. Archive does not exist: {_archivePath}");
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ProjectService.ResourceRegistry;
        var loadedProjectData = _projectDataService.LoadedProjectData;
        Guard.IsNotNull(loadedProjectData);

        var projectFolder = loadedProjectData.ProjectFolder;

        try
        {
            var extractDirectory = Path.GetFullPath(Path.Combine(projectFolder, FolderPath));

            ZipFile.ExtractToDirectory(_archivePath, extractDirectory);
            File.Delete(_archivePath);
            _archivePath = string.Empty;
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to undo folder delete. {ex.Message}");
        }

        //
        // Update the resource registry to add the restored folder
        //

        var updateResult = resourceRegistry.UpdateRegistry();
        if (updateResult.IsFailure)
        {
            return Result.Fail($"Failed to undo folder delete. {updateResult.Error}");
        }

        if (_folderWasExpanded)
        {
            // Expand the folder again if it was expanded when we deleted it
            resourceRegistry.SetExpandedFolders(new List<string>(){ FolderPath });
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
