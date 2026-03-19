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
        // If the inspector panel is active, try to undo the entity for the selected resource
        if (_workspaceWrapper.IsWorkspacePageLoaded)
        {
            var workspaceService = _workspaceWrapper.WorkspaceService;
            var activePanel = workspaceService.ActivePanel;

            if (activePanel == WorkspacePanel.Inspector)
            {
                var inspectorService = workspaceService.InspectorService;
                var inspectedResource = inspectorService.InspectedResource;

                if (!inspectedResource.IsEmpty)
                {
                    var entityService = workspaceService.EntityService;
                    if (entityService.GetUndoCount(inspectedResource) > 0)
                    {
                        entityService.UndoEntity(inspectedResource);
                        return Result.Ok();
                    }
                }
            }

            // Try file operation undo
            var resourceOpService = workspaceService.ResourceService.OperationService;
            if (resourceOpService.CanUndo)
            {
                _ = UndoFileOperationAsync();
                return Result.Ok();
            }
        }

        return Result.Ok();
    }

    private async Task UndoFileOperationAsync()
    {
        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceOpService = workspaceService.ResourceService.OperationService;

        var result = await resourceOpService.UndoAsync();
        if (result.IsSuccess)
        {
            // Trigger resource update to refresh the tree view and entity cache
            _commandService.Execute<IUpdateResourcesCommand>();
        }
    }

    public Result Redo()
    {
        // First try to redo the selected entity if the secondary panel is active
        if (_workspaceWrapper.IsWorkspacePageLoaded)
        {
            var workspaceService = _workspaceWrapper.WorkspaceService;
            var activePanel = workspaceService.ActivePanel;

            if (activePanel == WorkspacePanel.Inspector)
            {
                var inspectorService = workspaceService.InspectorService;
                var inspectedResource = inspectorService.InspectedResource;

                if (!inspectedResource.IsEmpty)
                {
                    var entityService = workspaceService.EntityService;
                    if (entityService.GetRedoCount(inspectedResource) > 0)
                    {
                        entityService.RedoEntity(inspectedResource);
                        return Result.Ok();
                    }
                }
            }

            // Try file operation redo
            var resourceOpService = workspaceService.ResourceService.OperationService;
            if (resourceOpService.CanRedo)
            {
                _ = RedoFileOperationAsync();
                return Result.Ok();
            }
        }

        return Result.Ok();
    }

    private async Task RedoFileOperationAsync()
    {
        var workspaceService = _workspaceWrapper.WorkspaceService;
        var resourceOpService = workspaceService.ResourceService.OperationService;

        var result = await resourceOpService.RedoAsync();
        if (result.IsSuccess)
        {
            // Trigger resource update to refresh the tree view and entity cache
            _commandService.Execute<IUpdateResourcesCommand>();
        }
    }
}
