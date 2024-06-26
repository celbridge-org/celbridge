using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Workspace;

namespace Celbridge.Project.Commands;

public class RefreshResourceTreeCommand : CommandBase, IRefreshResourceTreeCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public RefreshResourceTreeCommand(
        IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!_workspaceWrapper.IsWorkspaceLoaded)
        {
            return Result.Fail($"Failed to execute {nameof(RefreshResourceTreeCommand)} because workspace is not loaded");
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceRegistry = workspaceService.ProjectService.ResourceRegistry;

        var updateResult = resourceRegistry.UpdateRegistry();
        if (updateResult.IsFailure)
        {
            return Result.Fail($"Failed to execute {nameof(RefreshResourceTreeCommand)}. {updateResult.Error}");
        }

        await Task.CompletedTask;

        return Result.Ok();
    }
}
