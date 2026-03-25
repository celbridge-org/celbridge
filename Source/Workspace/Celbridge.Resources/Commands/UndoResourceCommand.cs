using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

public class UndoResourceCommand : CommandBase, IUndoResourceCommand
{
    public override CommandFlags CommandFlags => CommandFlags.UpdateResources;

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public UndoResourceCommand(IWorkspaceWrapper workspaceWrapper)
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

        if (!resourceOpService.CanUndo)
        {
            return Result.Fail("Nothing to undo");
        }

        return await resourceOpService.UndoAsync();
    }
}
