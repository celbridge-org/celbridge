using Celbridge.CommonUI.Views;

namespace Celbridge.CommonUI.UserInterface;

public interface IUserInterfaceService
{
    void Initialize(Window mainWindow, Frame frame);

    Window MainWindow { get; }

    Frame Frame { get; }

    WorkspaceView WorkspaceView { get; }
}
