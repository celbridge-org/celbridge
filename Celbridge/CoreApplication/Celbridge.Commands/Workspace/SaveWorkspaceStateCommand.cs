using Celbridge.BaseLibrary.Commands.Workspace;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Workspace;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Commands.Workspace;

public class SaveWorkspaceStateCommand : CommandBase, ISaveWorkspaceStateCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly IProjectDataService _projectDataService;

    public SaveWorkspaceStateCommand(
        IWorkspaceWrapper workspaceWrapper,
        IProjectDataService projectDataService)
    {
        _workspaceWrapper = workspaceWrapper;
        _projectDataService = projectDataService;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!_workspaceWrapper.IsWorkspaceLoaded)
        {
            return Result.Fail("Failed to Save Workspace State because workspace is not loaded");
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ProjectService.ResourceRegistry;

        //
        // Save the current expanded folders in the Resource Tree View
        //

        var expandedFolders = resourceRegistry.GetExpandedFolders();

        var workspaceData = _projectDataService.WorkspaceData;
        Guard.IsNotNull(workspaceData);

        var setFoldersResult = await workspaceData.SetExpandedFoldersAsync(expandedFolders);
        if (setFoldersResult.IsFailure)
        {
            return Result.Fail($"Failed to Save Workspace State. {setFoldersResult.Error}");
        }

        return Result.Ok();
    }
}
