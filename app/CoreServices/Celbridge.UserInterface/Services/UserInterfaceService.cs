using Microsoft.UI.Xaml.Controls.Primitives;
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
            SystemThemeHelper.SetRootTheme(_xamlRoot, value == UserInterfaceTheme.Dark);
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
    }
}
