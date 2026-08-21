using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Services;

public class UndoService : IUndoService
{
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public UndoService(
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;
    }

    public Result Undo()
    {
        if (_workspaceWrapper.IsWorkspaceLoaded)
        {
            var workspaceService = _workspaceWrapper.WorkspaceService;
            var resourceOpService = workspaceService.ResourceService.Operations;
            if (resourceOpService.CanUndo)
            {
                _ = UndoFileOperationAsync();
            }
        }

        return Result.Ok();
    }

    private async Task UndoFileOperationAsync()
    {
        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceOpService = workspaceService.ResourceService.Operations;

        var result = await resourceOpService.UndoAsync();
        if (result.IsSuccess)
        {
            // Trigger resource update to refresh the tree view
            _commandService.Execute<IUpdateResourcesCommand>();
        }
    }

    public Result Redo()
    {
        if (_workspaceWrapper.IsWorkspaceLoaded)
        {
            var workspaceService = _workspaceWrapper.WorkspaceService;
            var resourceOpService = workspaceService.ResourceService.Operations;
            if (resourceOpService.CanRedo)
            {
                _ = RedoFileOperationAsync();
            }
        }

        return Result.Ok();
    }

    private async Task RedoFileOperationAsync()
    {
        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceOpService = workspaceService.ResourceService.Operations;

        var result = await resourceOpService.RedoAsync();
        if (result.IsSuccess)
        {
            // Trigger resource update to refresh the tree view
            _commandService.Execute<IUpdateResourcesCommand>();
        }
    }
}
