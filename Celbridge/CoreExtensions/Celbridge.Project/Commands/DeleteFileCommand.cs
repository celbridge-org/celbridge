using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Utilities;
using Celbridge.BaseLibrary.Workspace;
using Celbridge.Utilities.Services;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Localization;
using System.IO.Compression;
using Windows.Storage;

namespace Celbridge.Project.Commands;

public class DeleteFileCommand : CommandBase, IDeleteFileCommand
{
    public override string StackName => CommandStackNames.Project;

    public string FilePath { get; set; } = string.Empty;

    private string _archivePath = string.Empty;

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

            // Generate a random file name for the archive
            _archivePath = _utilityService.GetTemporaryFilePath(PathConstants.DeletedFilesFolder, ".zip");
            if (File.Exists(_archivePath))
            {
                File.Delete(_archivePath);
            }

            var archiveFolder = Path.GetDirectoryName(_archivePath)!;
            Directory.CreateDirectory(archiveFolder);

            // Archive the file to temporary storage so we can undo the command
            using (var archive = ZipFile.Open(_archivePath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(deleteFilePath, FilePath);
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

    public override async Task<Result> UndoAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to undo delete file because workspace is not loaded");
        }

        if (!File.Exists(_archivePath))
        {
            return Result.Fail($"Failed to undo file delete. Archive does not exist: {_archivePath}");
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ProjectService.ResourceRegistry;
        var loadedProjectData = _projectDataService.LoadedProjectData;
        Guard.IsNotNull(loadedProjectData);

        var projectFolder = loadedProjectData.ProjectFolder;

        try
        {
            ZipFile.ExtractToDirectory(_archivePath, projectFolder);
            File.Delete(_archivePath);
            _archivePath = string.Empty;
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to undo file delete. {ex.Message}");
        }

        //
        // Update the resource registry to add the restored file
        //

        var updateResult = resourceRegistry.UpdateRegistry();
        if (updateResult.IsFailure)
        {
            return Result.Fail($"Failed to undo file delete. {updateResult.Error}");
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
