using Celbridge.CommonUI.Messages;
using Celbridge.CommonUI.Views;

namespace Celbridge.CommonUI.UserInterface;

public class UserInterfaceService : IUserInterfaceService
{
    private IMessengerService _messengerService;

    private Window? _mainWindow;
    private WorkspaceView? _workspaceView;

    public UserInterfaceService(IMessengerService messengerService)
    {
        _messengerService = messengerService;
    }

    public void Initialize(Window mainWindow, WorkspaceView workspaceView)
    {
        Guard.IsNotNull(mainWindow);
        Guard.IsNotNull(workspaceView);

        _mainWindow = mainWindow;
        _workspaceView = workspaceView;
    }

    public Window MainWindow => _mainWindow!;

    public WorkspaceView WorkspaceView => _workspaceView!;
}
