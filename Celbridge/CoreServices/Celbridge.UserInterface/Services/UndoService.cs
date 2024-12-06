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

    public Result<bool> Undo()
    {
        // First try to undo the selected entity if the inspector panel is active
        if (_workspaceWrapper.IsWorkspacePageLoaded)
        {
            var activePanel = _workspaceWrapper.WorkspaceService.ActivePanel;
            if (activePanel == WorkspacePanel.Inspector)
            {
                var explorerService = _workspaceWrapper.WorkspaceService.ExplorerService;
                var selectedResource = explorerService.SelectedResource;

                if (!selectedResource.IsEmpty)
                {
                    var entityService = _workspaceWrapper.WorkspaceService.EntityService;
                    return entityService.UndoEntity(selectedResource);
                }
            }
        }

        // If no entity was selected, try to undo the last command
        return _commandService.Undo();
    }

    public Result<bool> Redo()
    {
        // First try to redo the selected entity if the inspector panel is active
        if (_workspaceWrapper.IsWorkspacePageLoaded)
        {
            var activePanel = _workspaceWrapper.WorkspaceService.ActivePanel;
            if (activePanel == WorkspacePanel.Inspector)
            {
                var explorerService = _workspaceWrapper.WorkspaceService.ExplorerService;
                var selectedResource = explorerService.SelectedResource;

                if (!selectedResource.IsEmpty)
                {
                    var entityService = _workspaceWrapper.WorkspaceService.EntityService;
                    return entityService.RedoEntity(selectedResource);
                }
            }
        }

        // If no entity was selected, try to redo the last command
        return _commandService.Redo();
    }
}
