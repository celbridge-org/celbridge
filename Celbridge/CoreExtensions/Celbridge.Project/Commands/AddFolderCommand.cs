using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Utilities;
using Celbridge.BaseLibrary.Workspace;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Localization;

namespace Celbridge.Project.Commands;

public class AddFolderCommand : CommandBase, IAddFolderCommand
{
    public string FolderPath { get; set; } = string.Empty;

    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectDataService _projectDataService;
    private readonly IUtilityService _utilityService;
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;

    public AddFolderCommand(
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
        var createResult = await CreateFolder();
        if (createResult.IsFailure)
        {
            var titleText = _stringLocalizer.GetString("ResourceTree_AddFolder");
            var bodyText = _stringLocalizer.GetString("ResourceTree_FailedToCreateFolder", FolderPath);

            // Show alert
            await _dialogService.ShowAlertDialogAsync(titleText, bodyText);
        }

        return createResult;
    }

    private async Task<Result> CreateFolder()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to create folder because workspace is not loaded");
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
            return Result.Fail("Failed to create folder. Folder path is empty");
        }

        var segments = FolderPath.Split('/');
        foreach (var segment in segments)
        {
            if (!_utilityService.IsPathSegmentValid(segment))
            {
                return Result.Fail($"Failed to create folder. Folder name contains invalid characters");
            }
        }

        //
        // Create the folder on disk
        //

        try
        {
            var projectFolder = loadedProjectData.ProjectFolder;
            var newFolderPath = Path.Combine(projectFolder, FolderPath);
            newFolderPath = Path.GetFullPath(newFolderPath); // Make separators consistent

            if (Directory.Exists(newFolderPath))
            {
                return Result.Fail($"Failed to create folder. Folder already exists: {newFolderPath}");
            }

            Directory.CreateDirectory(newFolderPath);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to create folder. {ex.Message}");
        }

        //
        // Update the registry to include the new folder
        //

        var updateResult = resourceRegistry.UpdateRegistry();
        if (updateResult.IsFailure)
        {
            return Result.Fail($"Failed to create folder. {updateResult.Error}");
        }

        //
        // Expand the folder containing the newly created folder
        //
        int lastSlashIndex = FolderPath.LastIndexOf('/');
        if (lastSlashIndex > -1)
        {
            var parentFolder = FolderPath.Substring(0, lastSlashIndex);
            if (!string.IsNullOrEmpty(parentFolder))
            {
                var expandedFolders = new List<string>()
                {
                    parentFolder
                };
                resourceRegistry.SetExpandedFolders(expandedFolders);
            }
        }

        await Task.CompletedTask;

        return Result.Ok();
    }

    public static void AddFolder(string folderPath)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IAddFolderCommand>(command => command.FolderPath = folderPath);
    }
}
