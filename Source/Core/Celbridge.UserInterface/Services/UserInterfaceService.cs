using Celbridge.Logging;
using Celbridge.Platform;
using Celbridge.Settings;
using Celbridge.WebHost;

namespace Celbridge.UserInterface.Services;

public class UserInterfaceService : IUserInterfaceService
{
    private readonly ILogger<UserInterfaceService> _logger;
    private IMessengerService _messengerService;
    private ISettingsService _settingsService;
    private IWebViewStateService _webViewStateService;
    private readonly IPlatformInfo _platformInfo;
    private readonly IWindowActivationMonitor _windowActivationMonitor;

    private Window? _mainWindow;
    private XamlRoot? _xamlRoot;
    private ITitleBar? _titleBar;
    private ApplicationPage _activePage = ApplicationPage.None;
    private ThemeHelper? _themeHelper;
    private Helpers.WindowStateHelper? _windowStateHelper;

    public object MainWindow => _mainWindow!;
    public object XamlRoot => _xamlRoot!;
    public ITitleBar? TitleBar => _titleBar;
    public ApplicationPage ActivePage => _activePage;

    public UserInterfaceService(
        ILogger<UserInterfaceService> logger,
        IMessengerService messengerService,
        ISettingsService settingsService,
        IWebViewStateService webViewStateService,
        Helpers.WindowStateHelper windowStateHelper,
        IPlatformInfo platformInfo,
        IWindowActivationMonitor windowActivationMonitor)
    {
        _logger = logger;
        _messengerService = messengerService;
        _settingsService = settingsService;
        _webViewStateService = webViewStateService;
        _windowStateHelper = windowStateHelper;
        _platformInfo = platformInfo;
        _windowActivationMonitor = windowActivationMonitor;
    }

    public Result Initialize(object mainWindow, object xamlRoot)
    {
        _logger.LogDebug("Initializing UserInterfaceService");

        // Ensure these are only set once
        Guard.IsNull(_mainWindow);
        Guard.IsNull(_xamlRoot);

        if (mainWindow is not Window window)
        {
            var error = Result.Fail("MainWindow is not a Window instance");
            _logger.LogError(error.DiagnosticReport);
            return error;
        }

        if (xamlRoot is not XamlRoot root)
        {
            var error = Result.Fail("XamlRoot is not a XamlRoot instance");
            _logger.LogError(error.DiagnosticReport);
            return error;
        }

        _mainWindow = window;
        _xamlRoot = root;

        // Initialize platform-specific theme detection and titlebar management
        _themeHelper = new ThemeHelper(_mainWindow, _platformInfo);
        _themeHelper.Initialize(OnSystemThemeChanged);

        // Initialize window state management. A failure here is non-fatal: window geometry and
        // maximize-state restore are a convenience, not a startup requirement, so log and continue
        // with the default window placement rather than aborting initialization.
        Guard.IsNotNull(_windowStateHelper);
        var windowStateResult = _windowStateHelper.Initialize(_mainWindow);
        if (windowStateResult.IsFailure)
        {
            _logger.LogWarning("Failed to initialize window state management: {Error}", windowStateResult.DiagnosticReport);
        }

        // Broadcast a message whenever the main window acquires or loses focus, driving the custom title
        // bar's active/inactive tint. The monitor is a no-op on heads that draw a native title bar the OS
        // tints itself.
        _windowActivationMonitor.Start(_mainWindow);

        ApplyCurrentTheme();

        // The macOS Skia head ships only a minimal default app menu, so populate the native menubar with
        // the standard App/File/Edit/Window/Help menus. No-op on platforms without a native menu bar.
        if (_platformInfo.UsesNativeMenuBar)
        {
            var menuInstalled = Platform.MacOSMainMenu.Install();
            if (!menuInstalled)
            {
                _logger.LogWarning("Failed to install the native macOS menubar");
            }
        }

        _logger.LogDebug("UserInterfaceService initialized successfully");
        return Result.Ok();
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

    private void OnSystemThemeChanged(UserInterfaceTheme newTheme)
    {
        // Only apply theme changes if the app is configured to follow system theme
        if (_settingsService.Get(SettingCatalog.Application.Theme) != ApplicationColorTheme.System)
        {
            return;
        }

        // Check if the theme actually changed
        if (UserInterfaceTheme == newTheme)
        {
            return;
        }

        _logger.LogInformation("System theme changed to {Theme}", newTheme);
        ApplyCurrentTheme();
    }

    public void RegisterTitleBar(ITitleBar titleBar)
    {
        _titleBar = titleBar;
    }

    public void ApplyCurrentTheme()
    {
        var theme = _settingsService.Get(SettingCatalog.Application.Theme);
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

        _logger.LogInformation("Applied theme: {Theme} (setting: {Setting})", UserInterfaceTheme, theme);

        // Notify in-app (XAML) components that the theme has changed
        var message = new ThemeChangedMessage(UserInterfaceTheme);
        _messengerService.Send(message);

        // Publish to WebView clients (editors + console) via the app-state store. New WebViews pick
        // this up in their connect snapshot; open ones receive the broadcast.
        _webViewStateService.AppState.SetValue("theme", UserInterfaceTheme.ToString());

        // Update titlebar buttons
        _themeHelper?.UpdateTitleBar(UserInterfaceTheme);
    }

    public void SetActivePage(ApplicationPage page)
    {
        if (_activePage != page)
        {
            _activePage = page;
            var message = new ActivePageChangedMessage(page);
            _messengerService.Send(message);
        }
    }
}
