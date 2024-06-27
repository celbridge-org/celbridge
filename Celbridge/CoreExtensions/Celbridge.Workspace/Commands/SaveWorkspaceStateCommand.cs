using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Workspace;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Celbridge.Workspace.Commands;

public class SaveWorkspaceStateCommand : CommandBase, ISaveWorkspaceStateCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public SaveWorkspaceStateCommand(
        IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail("Failed to Save Workspace State because workspace is not loaded");
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ProjectService.ResourceRegistry;

        //
        // Save the current expanded folders in the Resource Tree View
        //

        var expandedFolders = resourceRegistry.GetExpandedFolders();

        var workspaceData = workspaceService.WorkspaceDataService.LoadedWorkspaceData;
        Guard.IsNotNull(workspaceData);

        var setFoldersResult = await workspaceData.SetExpandedFoldersAsync(expandedFolders);
        if (setFoldersResult.IsFailure)
        {
            return Result.Fail($"Failed to Save Workspace State. {setFoldersResult.Error}");
        }

        return Result.Ok();
    }

    public static void SaveWorkspaceState()
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<ISaveWorkspaceStateCommand>();
    }
}
