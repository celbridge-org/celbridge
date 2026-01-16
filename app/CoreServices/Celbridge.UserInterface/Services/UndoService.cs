using Celbridge.Commands;
using Celbridge.Entities;
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
        // If the secondary panel is active, try to undo the entity for the selected resource
        if (_workspaceWrapper.IsWorkspacePageLoaded)
        {
            var activePanel = _workspaceWrapper.WorkspaceService.ActivePanel;
            if (activePanel == WorkspacePanel.Secondary)
            {
                var explorerService = _workspaceWrapper.WorkspaceService.ExplorerService;
                var selectedResource = explorerService.SelectedResource;

                if (!selectedResource.IsEmpty)
                {
                    var entityService = _workspaceWrapper.WorkspaceService.EntityService;
                    if (entityService.GetUndoCount(selectedResource) > 0)
                    {
                        // Executing as a command ensures that no other operations are performed at the same time.
                        _commandService.Execute<IUndoEntityCommand>(command =>
                        {
                            command.Resource = selectedResource;
                        });
                    }

                    return Result.Ok();
                }
            }
        }

        // The undo is performed internally by executing a command.
        // Again, this ensures that no other operations are performed at the same time.
        return _commandService.Undo();
    }

    public Result Redo()
    {
        // First try to redo the selected entity if the secondary panel is active
        if (_workspaceWrapper.IsWorkspacePageLoaded)
        {
            var activePanel = _workspaceWrapper.WorkspaceService.ActivePanel;
            if (activePanel == WorkspacePanel.Secondary)
            {
                var explorerService = _workspaceWrapper.WorkspaceService.ExplorerService;
                var selectedResource = explorerService.SelectedResource;

                if (!selectedResource.IsEmpty)
                {
                    var entityService = _workspaceWrapper.WorkspaceService.EntityService;
                    if (entityService.GetRedoCount(selectedResource) > 0)
                    {
                        // Executing as a command ensures that no other operations are performed at the same time.
                        _commandService.Execute<IRedoEntityCommand>(command =>
                        {
                            command.Resource = selectedResource;
                        });
                    }

                    return Result.Ok();
                }
            }
        }

        // The redo is performed internally by executing a command.
        // Again, this ensures that no other operations are performed at the same time.
        return _commandService.Redo();
    }
}
