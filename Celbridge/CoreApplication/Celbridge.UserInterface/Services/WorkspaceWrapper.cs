using Celbridge.BaseLibrary.Workspace;

namespace Celbridge.UserInterface.Services;

public class WorkspaceWrapper : IWorkspaceWrapper
{
    private IMessengerService _messengerService;
    private IWorkspaceService? _workspaceService;

    public WorkspaceWrapper(IMessengerService messengerService)
    {
        _messengerService = messengerService;
        _messengerService.Register<WorkspaceServiceCreatedMessage>(this, OnWorkspaceServiceCreated);
        _messengerService.Register<WorkspaceUnloadedMessage>(this, OnWorkspaceUnloadedMessage);
    }

    private void OnWorkspaceServiceCreated(object recipient, WorkspaceServiceCreatedMessage loadedMessage)
    {
        // The workspace service is populated at the start of the workspace loading process.
        // This allows other systems to access the workspace service while they are loading in.
        Guard.IsNull(_workspaceService);
        _workspaceService = loadedMessage.WorkspaceService;
    }

    private void OnWorkspaceUnloadedMessage(object recipient, WorkspaceUnloadedMessage message)
    {
        // Clear the reference to the workspace service when the workspace is unloaded.
        Guard.IsNotNull(_workspaceService);
        _workspaceService = null;
    }

    public bool IsWorkspaceLoaded => _workspaceService is not null;

    public IWorkspaceService WorkspaceService
    {
        get
        {
            if (_workspaceService is null)
            {
                throw new InvalidOperationException("Failed to acquire workspace because no project is loaded");
            }
            return _workspaceService;
        }
    }
}
