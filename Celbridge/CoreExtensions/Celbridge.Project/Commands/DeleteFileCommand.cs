using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.Utilities;
using Celbridge.BaseLibrary.Workspace;
using Celbridge.Utilities.Services;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Localization;
using System.IO.Compression;

namespace Celbridge.Project.Commands;

public class DeleteFileCommand : CommandBase, IDeleteFileCommand
{
    public override string UndoStackName => UndoStackNames.Project;

    public ResourceKey ResourceKey { get; set; }

    private string _archivePath = string.Empty;

    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectDataService _projectDataService;
    private readonly IUtilityService _utilityService;
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;

    public DeleteFileCommand(
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
        var deleteResult = await DeleteFileAsync();
        if (deleteResult.IsFailure)
        {
            var titleString = _stringLocalizer.GetString("ResourceTree_DeleteFile");
            var messageString = _stringLocalizer.GetString("ResourceTree_FailedToDeleteFile", ResourceKey);

            // Show alert
            await _dialogService.ShowAlertDialogAsync(titleString, messageString);
        }

        return deleteResult;
    }

    public override async Task<Result> UndoAsync()
    {
        var deleteResult = await UndoDeleteFileAsync();
        if (deleteResult.IsFailure)
        {
            var titleString = _stringLocalizer.GetString("ResourceTree_DeleteFile");
            var messageString = _stringLocalizer.GetString("ResourceTree_FailedToUndoDeleteFile", ResourceKey);

            // Show alert
            await _dialogService.ShowAlertDialogAsync(titleString, messageString);
        }

        return deleteResult;
    }

    private async Task<Result> DeleteFileAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to delete file. Workspace is not loaded");
        }

        var loadedProjectData = _projectDataService.LoadedProjectData;

        Guard.IsNotNull(loadedProjectData);

        //
        // Validate the resource key
        //

        if (ResourceKey.IsEmpty)
        {
            return Result.Fail("Failed to delete file. Resource key is empty");
        }

        //
        // Delete the file on disk
        //

        try
        {
            var projectFolderPath = loadedProjectData.ProjectFolderPath;
            var deleteFilePath = Path.Combine(projectFolderPath, ResourceKey);
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

            var archiveFolderPath = Path.GetDirectoryName(_archivePath)!;
            Directory.CreateDirectory(archiveFolderPath);

            // Archive the file to temporary storage so we can undo the command
            using (var archive = ZipFile.Open(_archivePath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(deleteFilePath, ResourceKey);
            }

            File.Delete(deleteFilePath);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to delete file. {ex.Message}");
        }

        var message = new RequestResourceTreeUpdate();
        _messengerService.Send(message);

        await Task.CompletedTask;

        return Result.Ok();
    }

    private async Task<Result> UndoDeleteFileAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to undo file delete. Workspace is not loaded");
        }

        if (!File.Exists(_archivePath))
        {
            return Result.Fail($"Failed to undo file delete. Archive does not exist: {_archivePath}");
        }

        var loadedProjectData = _projectDataService.LoadedProjectData;
        Guard.IsNotNull(loadedProjectData);

        var projectFolderPath = loadedProjectData.ProjectFolderPath;

        try
        {
            ZipFile.ExtractToDirectory(_archivePath, projectFolderPath);
            File.Delete(_archivePath);
            _archivePath = string.Empty;
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to undo file delete. {ex.Message}");
        }

        var message = new RequestResourceTreeUpdate();
        _messengerService.Send(message);

        await Task.CompletedTask;

        return Result.Ok();
    }

    public static void DeleteFile(string resourceKey)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IDeleteFileCommand>(command => command.ResourceKey = resourceKey);
    }
}
