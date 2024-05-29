using Celbridge.BaseLibrary.UserInterface;
using Celbridge.BaseLibrary.UserInterface.Dialog;
using Celbridge.BaseLibrary.UserInterface.FilePicker;
using Celbridge.BaseLibrary.Workspace;

namespace Celbridge.Services.UserInterface;

public class UserInterfaceService : IUserInterfaceService
{
    private IMessengerService _messengerService;
    private ILoggingService _loggingService;
    private IWorkspaceService? _workspaceService;

    private Window? _mainWindow;
    private XamlRoot? _xamlRoot;
    public object MainWindow => _mainWindow!;
    public object XamlRoot => _xamlRoot!;

    //
    // These properties provide convenient access to various user interface related services
    //
    public IFilePickerService FilePickerService { get; private set; }
    public IDialogService DialogService { get; private set;  }

    public UserInterfaceService(
        IMessengerService messengerService, 
        ILoggingService loggingService,
        IFilePickerService filePickerService,
        IDialogService dialogService)
    {
        _messengerService = messengerService;
        _loggingService = loggingService;
        FilePickerService = filePickerService;
        DialogService = dialogService;
    }

    public void Initialize(Window mainWindow, XamlRoot xamlRoot)
    {
        // Ensure these are only set once
        Guard.IsNull(_mainWindow);
        Guard.IsNull(_xamlRoot);

        _mainWindow = mainWindow;
        _xamlRoot = xamlRoot;

#if WINDOWS
        // Broadcast a message whenever the main window acquires or loses focus (Windows only).
        _mainWindow.Activated += MainWindow_Activated;
#endif

        _messengerService.Register<WorkspaceServiceCreatedMessage>(this, OnWorkspaceServiceCreated);
        _messengerService.Register<WorkspaceServiceDestroyedMessage>(this, OnWorkspaceServiceDestroyed);
    }

#if WINDOWS
    private void MainWindow_Activated(object sender, WindowActivatedEventArgs e)
    {
        var activationState = e.WindowActivationState;

        if (activationState == WindowActivationState.Deactivated)
        {
            var message = new MainWindowDeactivatedMessage();
            _messengerService.Send(message);
        }
        else if (activationState == WindowActivationState.PointerActivated ||
                 activationState == WindowActivationState.CodeActivated)
        {
            var message = new MainWindowActivatedMessage();
            _messengerService.Send(message);
        }
    }
#endif

    private void OnWorkspaceServiceCreated(object recipient, WorkspaceServiceCreatedMessage loadedMessage)
    {
        // Comment out this assert to enable hot reload
        Guard.IsNull(_workspaceService);
        _workspaceService = loadedMessage.WorkspaceService;
    }

    private void OnWorkspaceServiceDestroyed(object recipient, WorkspaceServiceDestroyedMessage message)
    {
        // Comment out this assert to enable hot reload
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
