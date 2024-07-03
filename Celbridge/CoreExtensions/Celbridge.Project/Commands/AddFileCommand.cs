using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Utilities;
using Celbridge.BaseLibrary.Workspace;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Localization;

namespace Celbridge.Project.Commands;

public class AddFileCommand : CommandBase, IAddFileCommand
{
    public override string StackName => CommandStackNames.Project;

    public string FilePath { get; set; } = string.Empty;

    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectDataService _projectDataService;
    private readonly IUtilityService _utilityService;
    private readonly IDialogService _dialogService;
    private readonly IStringLocalizer _stringLocalizer;

    public AddFileCommand(
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
        var createResult = await CreateFile();
        if (createResult.IsFailure)
        {
            var titleText = _stringLocalizer.GetString("ResourceTree_AddFile");
            var bodyText = _stringLocalizer.GetString("ResourceTree_FailedToCreateFile", FilePath);

            // Show alert
            await _dialogService.ShowAlertDialogAsync(titleText, bodyText);
        }

        return createResult;
    }

    private async Task<Result> CreateFile()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to create file because workspace is not loaded");
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ProjectService.ResourceRegistry;
        var loadedProjectData = _projectDataService.LoadedProjectData;

        Guard.IsNotNull(loadedProjectData);

        //
        // Validate the folder path
        //

        if (string.IsNullOrEmpty(FilePath))
        {
            return Result.Fail("Failed to create file. Path is empty");
        }

        var segments = FilePath.Split('/');
        foreach (var segment in segments)
        {
            if (!_utilityService.IsPathSegmentValid(segment))
            {
                return Result.Fail($"Failed to create file. Path contains invalid characters");
            }
        }

        //
        // Create the file on disk
        //

        try
        {
            var projectFolder = loadedProjectData.ProjectFolder;
            var newFilePath = Path.Combine(projectFolder, FilePath);
            newFilePath = Path.GetFullPath(newFilePath); // Make separators consistent

            // It's important to fail if the file already exists, because undoing this command
            // deletes the file, which could lead to unexpected data loss.
            if (File.Exists(newFilePath))
            {
                return Result.Fail($"Failed to create file. File already exists: {newFilePath}");
            }

            File.WriteAllText(newFilePath, string.Empty);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to create file. {ex.Message}");
        }

        //
        // Update the resource registry to include the new file
        //

        var updateResult = resourceRegistry.UpdateRegistry();
        if (updateResult.IsFailure)
        {
            return Result.Fail($"Failed to create file. {updateResult.Error}");
        }

        //
        // Expand the folder containing the newly created file
        //
        int lastSlashIndex = FilePath.LastIndexOf('/');
        if (lastSlashIndex > -1)
        {
            var parentFolder = FilePath.Substring(0, lastSlashIndex);
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

    public override async Task<Result> UndoAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail($"Failed to undo create folder because workspace is not loaded");
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
            return Result.Fail("Failed to undo create file. File path is empty");
        }

        // We can assume the FilePath segments are valid because the command already executed successfully.

        //
        // Delete the file on disk
        //

        try
        {
            var projectFolder = loadedProjectData.ProjectFolder;
            var newFilePath = Path.Combine(projectFolder, FilePath);
            newFilePath = Path.GetFullPath(newFilePath); // Make separators consistent

            if (!File.Exists(newFilePath))
            {
                return Result.Fail($"Failed to undo create file. File does not exist: {newFilePath}");
            }

            File.Delete(newFilePath);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to undo create file. {ex.Message}");
        }

        //
        // Update the resource registry to remove the file
        //

        var updateResult = resourceRegistry.UpdateRegistry();
        if (updateResult.IsFailure)
        {
            return Result.Fail($"Failed to create file. {updateResult.Error}");
        }

        await Task.CompletedTask;

        return Result.Ok();
    }

    public static void AddFile(string filePath)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IAddFileCommand>(command => command.FilePath = filePath);
    }
}
