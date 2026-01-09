using Celbridge.Commands;

namespace Celbridge.Workspace.Commands;

public class ToggleAllPanelsCommand : CommandBase, IToggleAllPanelsCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ToggleAllPanelsCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail("Workspace is not loaded.");
        }

        var workspaceService = _workspaceWrapper.WorkspaceService;

        workspaceService.ToggleAllPanels();

        await Task.CompletedTask;

        return Result.Ok();
    }
}
