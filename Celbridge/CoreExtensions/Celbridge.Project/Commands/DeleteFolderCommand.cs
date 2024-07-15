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

    public ResourceKey ResourceKey { get; set; }

    private string _archivePath = string.Empty;
    private bool _folderWasEmpty;
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
            var messageString = _stringLocalizer.GetString("ResourceTree_FailedToDeleteFolder", ResourceKey);

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
        // Validate the resource key
        //

        if (string.IsNullOrEmpty(ResourceKey))
        {
            return Result.Fail("Failed to delete folder. Resource key is empty");
        }

        //
        // Delete the folder on disk
        //

        try
        {
            var projectFolderPath = loadedProjectData.ProjectFolderPath;
            var deleteFolderPath = Path.Combine(projectFolderPath, ResourceKey);
            deleteFolderPath = Path.GetFullPath(deleteFolderPath); // Make separators consistent

            if (!Directory.Exists(deleteFolderPath))
            {
                return Result.Fail($"Failed to delete folder. Folder does not exist: {deleteFolderPath}");
            }

            // Get the list of files and subdirectories in the folder
            var files = Directory.GetFiles(deleteFolderPath);
            var directories = Directory.GetDirectories(deleteFolderPath);
            if (files.Length == 0 && directories.Length == 0)
            {
                // There's no point archiving an empty folder, set a flag instead.
                _folderWasEmpty = true;
            }
            else
            {
                // Backup the folder to a zip archive so we can restore it if the user undoes the delete
                // Generate a random file name for the archive
                _archivePath = _utilityService.GetTemporaryFilePath(PathConstants.DeletedFilesFolder, ".zip");
                if (File.Exists(_archivePath))
                {
                    File.Delete(_archivePath);
                }

                var archiveFolderPath = Path.GetDirectoryName(_archivePath)!;
                Directory.CreateDirectory(archiveFolderPath);

                // Archive the folder to temporary storage so we can undo the command
                ZipFile.CreateFromDirectory(deleteFolderPath, _archivePath, CompressionLevel.Optimal, includeBaseDirectory: false);
            }

            // Record if the folder was expanded so we can expand it again in the undo if needed
            _folderWasExpanded = resourceRegistry.IsFolderExpanded(ResourceKey);

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

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ProjectService.ResourceRegistry;
        var loadedProjectData = _projectDataService.LoadedProjectData;
        Guard.IsNotNull(loadedProjectData);

        var projectFolderPath = loadedProjectData.ProjectFolderPath;

        try
        {
            var folderPath = Path.GetFullPath(Path.Combine(projectFolderPath, ResourceKey));

            if (_folderWasEmpty)
            {
                Directory.CreateDirectory(folderPath);
                _folderWasEmpty = false;
            }
            else
            {
                if (!File.Exists(_archivePath))
                {
                    return Result.Fail($"Failed to undo folder delete. Archive does not exist: {_archivePath}");
                }

                ZipFile.ExtractToDirectory(_archivePath, folderPath);
                File.Delete(_archivePath);
                _archivePath = string.Empty;
            }
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to undo folder delete. {ex.Message}");
        }

        // Expand the folder again if it was expanded when we deleted it
        resourceRegistry.SetFolderIsExpanded(ResourceKey, _folderWasExpanded);

        var message = new RequestResourceTreeUpdate();
        _messengerService.Send(message);

        await Task.CompletedTask;

        return Result.Ok();
    }

    public static void DeleteFolder(string resourceKey)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IDeleteFolderCommand>(command => command.ResourceKey = resourceKey);
    }
}
