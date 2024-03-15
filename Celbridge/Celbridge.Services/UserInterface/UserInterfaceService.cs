using Celbridge.BaseLibrary.UserInterface;

namespace Celbridge.Services.UserInterface;

public class UserInterfaceService : IUserInterfaceService
{
    private IMessengerService _messengerService;
    private ILoggingService _loggingService;
    private IWorkspaceService? _workspaceService;

    public UserInterfaceService(IMessengerService messengerService, 
        ILoggingService loggingService)
    {
        _messengerService = messengerService;
        _loggingService = loggingService;
    }

    public void Initialize()
    {
        _messengerService.Register<WorkspaceLoadedMessage>(this, OnWorkspaceLoaded);
        _messengerService.Register<WorkspaceUnloadedMessage>(this, OnWorkspaceUnloaded);
    }

    private void OnWorkspaceLoaded(object recipient, WorkspaceLoadedMessage loadedMessage)
    {
        Guard.IsNull(_workspaceService);
        _workspaceService = loadedMessage.WorkspaceService;
    }

    private void OnWorkspaceUnloaded(object recipient, WorkspaceUnloadedMessage message)
    {
        Guard.IsNotNull(_workspaceService);
        _workspaceService = null;
    }

    public IWorkspaceService WorkspaceService
    {
        get
        {
            if (_workspaceService is null)
            {
                throw new InvalidOperationException("Failed to acquire workspace because no workspace has been loaded");
            }
            return _workspaceService;
        }
    }

    /// 
    /// All the workspace panel configurations must be registered before we can load the workspace, so they are registered
    /// with the user interface service which has the same lifetime scope as the application.
    /// 
    private List<WorkspacePanelConfig> _workspacePanelConfigs = new();
    public IEnumerable<WorkspacePanelConfig> WorkspacePanelConfigs => _workspacePanelConfigs;

    public Result RegisterWorkspacePanelConfig(WorkspacePanelConfig workspacePanelConfig)
    {
        foreach (var config in _workspacePanelConfigs)
        {
            if (config.PanelType == workspacePanelConfig.PanelType)
            {
                var errorMessage = $"Panel type '{workspacePanelConfig.PanelType}' is already registered.";
                _loggingService.Error(errorMessage);

                return Result.Fail(errorMessage);
            }
        }

        _workspacePanelConfigs.Add(workspacePanelConfig);
        return Result.Ok();
    }
}
