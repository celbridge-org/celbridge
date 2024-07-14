using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Resources;
using Celbridge.BaseLibrary.Utilities;
using Celbridge.BaseLibrary.Workspace;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Localization;
using Windows.Storage;

namespace Celbridge.Project.Commands;

public class AddFileCommand : CommandBase, IAddFileCommand
{
    public override string StackName => CommandStackNames.Project;

    public string ResourcePath { get; set; } = string.Empty;

    private readonly IMessengerService _messengerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectDataService _projectDataService;
    private readonly IUtilityService _utilityService;
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;

    public AddFileCommand(
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
        var createResult = await CreateFile();
        if (createResult.IsFailure)
        {
            var titleString = _stringLocalizer.GetString("ResourceTree_AddFile");
            var messageString = _stringLocalizer.GetString("ResourceTree_FailedToCreateFile", ResourcePath);

            // Show alert
            await _dialogService.ShowAlertDialogAsync(titleString, messageString);
        }

        return createResult;
    }

    private async Task<Result> CreateFile()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to add file because workspace is not loaded");
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
            return Result.Fail("Failed to add file. Resource path is empty");
        }

        if (!_utilityService.IsValidResourcePath(ResourcePath))
        {
            return Result.Fail($"Failed to add file. Resource path {ResourcePath} is not valid.");
        }

        //
        // Create the file on disk
        //

        try
        {
            var projectFolder = loadedProjectData.ProjectFolder;
            var newFilePath = Path.GetFullPath(Path.Combine(projectFolder, ResourcePath));

            var folderPath = Path.GetDirectoryName(newFilePath);
            var fileName = Path.GetFileName(newFilePath);

            if (folderPath == null || fileName == null)
            {
                return Result.Fail("Invalid folder path or file name.");
            }

            var storageFolder = await StorageFolder.GetFolderFromPathAsync(folderPath);

            if (await storageFolder.TryGetItemAsync(fileName) != null)
            {
                // It's important to fail if the file already exists, because undoing this command
                // deletes the file, which could lead to unexpected data loss.

                return Result.Fail($"Failed to add file. A file already exists at '{newFilePath}'.");
            }

            // Create the file
            await storageFolder.CreateFileAsync(fileName, CreationCollisionOption.FailIfExists);

            // No need to write to the file as it is created empty
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to add file. {ex.Message}");
        }


        //
        // Expand the folder containing the newly created file
        //
        int lastSlashIndex = ResourcePath.LastIndexOf('/');
        if (lastSlashIndex > -1)
        {
            var parentFolder = ResourcePath.Substring(0, lastSlashIndex);
            if (!string.IsNullOrEmpty(parentFolder))
            {
                resourceRegistry.SetFolderIsExpanded(parentFolder, true);
            }
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
            return Result.Fail($"Failed to undo add file. Workspace is not loaded");
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
            return Result.Fail("Failed to undo add file. Resource path is empty");
        }

        // We can assume the resource path is valid because the command already executed successfully.

        //
        // Delete the file on disk
        //

        try
        {
            var newFilePath = Path.GetFullPath(Path.Combine(loadedProjectData.ProjectFolder, ResourcePath));

            var folderPath = Path.GetDirectoryName(newFilePath);
            var fileName = Path.GetFileName(newFilePath);

            if (folderPath == null || fileName == null)
            {
                return Result.Fail("Invalid folder path or file name.");
            }

            var storageFolder = await StorageFolder.GetFolderFromPathAsync(folderPath);

            var fileStorage = await storageFolder.TryGetItemAsync(fileName);
            if (fileStorage == null)
            {
                return Result.Fail($"Failed to undo add file. File does not exist at '{newFilePath}'.");
            }

            await fileStorage.DeleteAsync();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to undo add file. {ex.Message}");
        }

        var message = new RequestResourceTreeUpdate();
        _messengerService.Send(message);

        await Task.CompletedTask;

        return Result.Ok();
    }

    public static void AddFile(string resourcePath)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IAddFileCommand>(command => command.ResourcePath = resourcePath);
    }
}
