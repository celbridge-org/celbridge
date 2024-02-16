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
    }

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
