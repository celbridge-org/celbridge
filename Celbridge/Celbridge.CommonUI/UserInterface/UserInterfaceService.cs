using Celbridge.CommonUI.Messages;
using Celbridge.CommonUI.Views;

namespace Celbridge.CommonUI.UserInterface;

public class UserInterfaceService : IUserInterfaceService
{
    private IMessengerService _messengerService;

    private Window? _mainWindow;
    private Frame? _frame;
    private WorkspaceView? _workspaceView;

    public UserInterfaceService(IMessengerService messengerService)
    {
        _messengerService = messengerService;

        _messengerService.Register<WorkspaceViewLoadedMessage>(this, OnWorkspaceViewLoaded);
        _messengerService.Register<WorkspaceViewUnloadedMessage>(this, OnWorkspaceViewUnloaded);
    }

    public void Initialize(Window mainWindow, Frame frame)
    {
        Guard.IsNotNull(mainWindow);
        Guard.IsNotNull(frame);

        _mainWindow = mainWindow;
        _frame = frame;

#if WINDOWS
        // Broadcast a message whenever the main window acquires or loses focus (Windows only).
        _mainWindow.Activated += MainWindow_Activated;
#endif
    }

#if WINDOWS
    private void MainWindow_Activated(object sender, WindowActivatedEventArgs e)
    {
        var activationState = e.WindowActivationState;

        if (activationState == WindowActivationState.Deactivated)
        {
            var message = new MainWindowDeactivated();
            _messengerService.Send(message);
        }
        else if (activationState == WindowActivationState.PointerActivated ||
                 activationState == WindowActivationState.CodeActivated)
        {
            var message = new MainWindowActivated();
            _messengerService.Send(message);
        }
    }
#endif

    private void OnWorkspaceViewLoaded(object recipient, WorkspaceViewLoadedMessage message)
    {
        _workspaceView = message.workspace;
    }

    private void OnWorkspaceViewUnloaded(object recipient, WorkspaceViewUnloadedMessage message)
    {
        _workspaceView = null;
    }

    public Window MainWindow => _mainWindow!;
    public Frame Frame => _frame!;
    public WorkspaceView WorkspaceView => _workspaceView!;
}
