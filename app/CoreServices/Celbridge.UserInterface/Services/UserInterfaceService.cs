using Celbridge.Settings;

namespace Celbridge.UserInterface.Services;

public class UserInterfaceService : IUserInterfaceService
{
    private IMessengerService _messengerService;
    private IEditorSettings _editorSettings;

    private Window? _mainWindow;
    private XamlRoot? _xamlRoot;
    private Celbridge.UserInterface.Views.TitleBar? _titleBar;

    public object MainWindow => _mainWindow!;
    public object XamlRoot => _xamlRoot!;
    public object TitleBar => _titleBar!;

    public UserInterfaceService(
        IMessengerService messengerService, 
        IEditorSettings editorSettings)
    {
        _messengerService = messengerService;
        _editorSettings = editorSettings;
    }

    public void Initialize(Window mainWindow, XamlRoot xamlRoot)
    {
        // Ensure these are only set once
        Guard.IsNull(_mainWindow);
        Guard.IsNull(_xamlRoot);

        _mainWindow = mainWindow;
        _xamlRoot = xamlRoot;

#if WINDOWS
        // Restore window maximized state
        RestoreWindowState();

        // Track window state changes
        var appWindow = GetAppWindow(_mainWindow);
        if (appWindow != null)
        {
            appWindow.Changed += AppWindow_Changed;
        }
#endif

        ApplyCurrentTheme();

#if WINDOWS
        // Broadcast a message whenever the main window acquires or loses focus (Windows only).
        _mainWindow.Activated += MainWindow_Activated;
#endif
    }

    public UserInterfaceTheme UserInterfaceTheme
    {
        get
        {
            var rootTheme = SystemThemeHelper.GetRootTheme(_xamlRoot);
            return rootTheme == Microsoft.UI.Xaml.ApplicationTheme.Light ? UserInterfaceTheme.Light : UserInterfaceTheme.Dark;
        }

        set 
        {
            switch (value)
            {
                case UserInterfaceTheme.Dark:
                    SystemThemeHelper.SetApplicationTheme(_xamlRoot, ElementTheme.Dark);
                    break;

                case UserInterfaceTheme.Light:
                    SystemThemeHelper.SetApplicationTheme(_xamlRoot, ElementTheme.Light);
                    break;

                default:
                    SystemThemeHelper.SetApplicationTheme(_xamlRoot, ElementTheme.Default);
                    break;
            }
        }
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

    private void RestoreWindowState()
    {
        var appWindow = GetAppWindow(_mainWindow);
        if (appWindow == null) return;

        // Restore maximized state
        if (_editorSettings.IsWindowMaximized)
        {
            var presenter = appWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
            if (presenter != null)
            {
                presenter.Maximize();
            }
        }
    }

    private void AppWindow_Changed(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowChangedEventArgs args)
    {
        // Save maximized state when it changes
        if (args.DidPresenterChange)
        {
            var presenter = sender.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
            if (presenter != null)
            {
                _editorSettings.IsWindowMaximized = presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized;
            }
        }
    }

    private Microsoft.UI.Windowing.AppWindow? GetAppWindow(Window? window)
    {
        if (window == null) return null;

        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
        return Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
    }
#endif

    public void RegisterTitleBar(object titleBar)
    {
        Views.TitleBar? givenTitleBar = titleBar as Views.TitleBar;
        if (givenTitleBar != null)
        {
            _titleBar = givenTitleBar;
        }
    }

    public void SetCurrentProjectTitle(string currentProjectTitle)
    {
        Guard.IsNotNull(_titleBar);
        _titleBar.SetProjectTitle(currentProjectTitle);
    }

    public void ApplyCurrentTheme()
    {
        var theme = _editorSettings.Theme;
        switch (theme)
        {
            case ApplicationColorTheme.System:
                switch (SystemThemeHelper.GetCurrentOsTheme())
                {
                    case ApplicationTheme.Dark:
                        UserInterfaceTheme = UserInterfaceTheme.Dark;
                        break;

                    case ApplicationTheme.Light:
                        UserInterfaceTheme = UserInterfaceTheme.Light;
                        break;

                    default:
                        break;
                }
                break;

            case ApplicationColorTheme.Dark:
                UserInterfaceTheme = UserInterfaceTheme.Dark;
                break;

            case ApplicationColorTheme.Light:
                UserInterfaceTheme = UserInterfaceTheme.Light;
                break;

            default:
                break;
        }

        // Notify all components that the theme has changed
        var message = new ThemeChangedMessage(UserInterfaceTheme);
        _messengerService.Send(message);
    }
}
