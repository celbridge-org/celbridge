using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Workspace;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Project.Commands;

public class AddFolderCommand : CommandBase, IAddFolderCommand
{
    public string FolderPath { get; set; } = string.Empty;

    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectDataService _projectDataService;

    public AddFolderCommand(
        IWorkspaceWrapper workspaceWrapper,
        IProjectDataService projectDataService)
    {
        _workspaceWrapper = workspaceWrapper;
        _projectDataService = projectDataService;
    }

    public override async Task<Result> ExecuteAsync()
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
        foreach (var segment in segments )
        {
            if (!_projectDataService.IsPathSegmentValid(segment))
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

        await Task.CompletedTask;

        return Result.Ok();
    }

    public static void AddFolder(string folderPath)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IAddFolderCommand>(command => command.FolderPath = folderPath);
    }
}
