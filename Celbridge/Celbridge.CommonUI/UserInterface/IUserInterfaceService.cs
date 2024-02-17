using Celbridge.CommonUI.Views;

namespace Celbridge.CommonUI.UserInterface;

public interface IUserInterfaceService
{
    void Initialize(Window mainWindow);

    void Navigate(Type page);

    Window MainWindow { get; }

    WorkspaceView WorkspaceView { get; }
}
