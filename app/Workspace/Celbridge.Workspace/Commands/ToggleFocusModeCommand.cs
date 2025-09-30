using Celbridge.Commands;

namespace Celbridge.Workspace.Commands;

public class ToggleFocusModeCommand : CommandBase, IToggleFocusModeCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ToggleFocusModeCommand(IWorkspaceWrapper workspaceWrapper)
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

        workspaceService.ToggleFocusMode();

        await Task.CompletedTask;

        return Result.Ok();
    }
}
