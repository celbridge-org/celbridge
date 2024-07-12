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

public class DeleteFolderCommand : CommandBase, IDeleteFolderCommand
{
    public override string StackName => CommandStackNames.Project;

    public string ResourcePath { get; set; } = string.Empty;

    private string _archivePath = string.Empty;
    private bool _folderWasExpanded;

    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectDataService _projectDataService;
    private readonly IUtilityService _utilityService;
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;

    public DeleteFolderCommand(
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
        var createResult = await DeleteFolder();
        if (createResult.IsFailure)
        {
            var titleString = _stringLocalizer.GetString("ResourceTree_DeleteFolder");
            var messageString = _stringLocalizer.GetString("ResourceTree_FailedToDeleteFolder", ResourcePath);

            // Show alert
            await _dialogService.ShowAlertDialogAsync(titleString, messageString);
        }

        return createResult;
    }

    private async Task<Result> DeleteFolder()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to delete folder. Workspace is not loaded");
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ProjectService.ResourceRegistry;
        var loadedProjectData = _projectDataService.LoadedProjectData;

        Guard.IsNotNull(loadedProjectData);

        //
        // Validate the resource path
        //

        if (string.IsNullOrEmpty(ResourcePath))
        {
            return Result.Fail("Failed to delete folder. Resource path is empty");
        }

        //
        // Delete the folder on disk
        //

        try
        {
            var projectFolder = loadedProjectData.ProjectFolder;
            var deleteFolderPath = Path.Combine(projectFolder, ResourcePath);
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
            _folderWasExpanded = resourceRegistry.IsFolderExpanded(ResourcePath);

            Directory.Delete(deleteFolderPath, true);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to delete folder. {ex.Message}");
        }

        var message = new RequestResourceTreeUpdate();
        _messengerService.Send(message);

        await Task.CompletedTask;

        return Result.Ok();
    }

    public override async Task<Result> UndoAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to undo folder delete. Workspace is not loaded");
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
            var extractDirectory = Path.GetFullPath(Path.Combine(projectFolder, ResourcePath));

            ZipFile.ExtractToDirectory(_archivePath, extractDirectory);
            File.Delete(_archivePath);
            _archivePath = string.Empty;
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to undo folder delete. {ex.Message}");
        }

        // Expand the folder again if it was expanded when we deleted it
        resourceRegistry.SetFolderIsExpanded(ResourcePath, _folderWasExpanded);

        var message = new RequestResourceTreeUpdate();
        _messengerService.Send(message);

        await Task.CompletedTask;

        return Result.Ok();
    }

    public static void DeleteFolder(string resourcePath)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IDeleteFolderCommand>(command => command.ResourcePath = resourcePath);
    }
}
