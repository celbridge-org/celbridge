using Celbridge.CommonUI.Messages;
using Celbridge.CommonUI.Views;

namespace Celbridge.CommonUI.UserInterface;

public class UserInterfaceService : IUserInterfaceService
{
    private IMessengerService _messengerService;

    private Window? _mainWindow;
    public Window MainWindow => _mainWindow!;

    private MainPage? _mainPage;
    public MainPage MainPage => _mainPage!;

    private WorkspacePage? _workspacePage;
    public WorkspacePage WorkspacePage => _workspacePage!;

    public UserInterfaceService(IMessengerService messengerService)
    {
        _messengerService = messengerService;

        _messengerService.Register<MainPageLoadedMessage>(this, OnMainPageLoaded);
        _messengerService.Register<MainPageUnloadedMessage>(this, OnMainPageUnloaded);

        _messengerService.Register<WorkspacePageLoadedMessage>(this, OnWorkspaceViewLoaded);
        _messengerService.Register<WorkspacePageUnloadedMessage>(this, OnWorkspaceViewUnloaded);
    }

    public void Initialize(Window mainWindow)
    {
        Guard.IsNotNull(mainWindow);

        _mainWindow = mainWindow;

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

    public void Navigate<T>() where T : Page
    {
        Guard.IsNotNull(_mainPage);

        var pageType = typeof(T);
        _mainPage.Navigate(pageType);
    }

    private void OnMainPageLoaded(object recipient, MainPageLoadedMessage message)
    {
        _mainPage = message.mainPage;
    }

    private void OnMainPageUnloaded(object recipient, MainPageUnloadedMessage message)
    {
        _mainPage = null;
    }

    private void OnWorkspaceViewLoaded(object recipient, WorkspacePageLoadedMessage message)
    {
        _workspacePage = message.workspace;
    }

    private void OnWorkspaceViewUnloaded(object recipient, WorkspacePageUnloadedMessage message)
    {
        _workspacePage = null;
    }
}
