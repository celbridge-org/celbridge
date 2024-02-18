using Celbridge.CommonUI.Views;

namespace Celbridge.CommonUI.UserInterface;

public interface IUserInterfaceService
{
    void Initialize(Window mainWindow);

    void Navigate<T>() where T : Page;

    Window MainWindow { get; }

    WorkspaceView WorkspaceView { get; }
}
