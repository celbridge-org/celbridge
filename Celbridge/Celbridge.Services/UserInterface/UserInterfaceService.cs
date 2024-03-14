using Celbridge.BaseLibrary.UserInterface;

namespace Celbridge.Services.UserInterface;

public class UserInterfaceService : IUserInterfaceService
{
    private IMessengerService _messengerService;
    private ILoggingService _loggingService;

    public UserInterfaceService(IMessengerService messengerService, 
        ILoggingService loggingService)
    {
        _messengerService = messengerService;
        _loggingService = loggingService;
    }

    public void Initialize()
    {
        _messengerService.Register<WorkspacePageLoadedMessage>(this, OnWorkspacePageLoaded);
    }

    private void OnWorkspacePageLoaded(object recipient, WorkspacePageLoadedMessage loadedMessage)
    {
        _workspace = loadedMessage.Workspace;
    }

    private IWorkspace? _workspace;
    public IWorkspace Workspace
    {
        get
        {
            if (_workspace is null)
            {
                throw new InvalidOperationException("Failed to acquire workspace because no workspace has been loaded");
            }
            return _workspace;
        }
    }

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
