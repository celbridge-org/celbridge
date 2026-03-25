using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

public class RedoResourceCommand : CommandBase, IRedoResourceCommand
{
    public override CommandFlags CommandFlags => CommandFlags.UpdateResources;

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public RedoResourceCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail("Workspace is not loaded");
        }

        var resourceOpService = _workspaceWrapper.WorkspaceService.ResourceService.OperationService;

        if (!resourceOpService.CanRedo)
        {
            return Result.Fail("Nothing to redo");
        }

        return await resourceOpService.RedoAsync();
    }
}
