using Celbridge.Commands;

namespace Celbridge.Workspace.Commands;

public class ToggleZenModeCommand : CommandBase, IToggleZenModeCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ToggleZenModeCommand(IWorkspaceWrapper workspaceWrapper)
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

        workspaceService.ToggleZenMode();

        await Task.CompletedTask;

        return Result.Ok();
    }
}
