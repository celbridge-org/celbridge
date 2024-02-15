using Celbridge.CommonUI.Views;

namespace Celbridge.CommonUI.UserInterface;

public interface IUserInterfaceService
{
    void Initialize(Window mainWindow, WorkspaceView workspaceView);

    Window MainWindow { get; }

    WorkspaceView WorkspaceView { get; }
}
