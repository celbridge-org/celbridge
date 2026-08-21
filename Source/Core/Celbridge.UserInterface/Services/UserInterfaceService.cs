using System.Globalization;
using Celbridge.Logging;
using Celbridge.Platform;
using Celbridge.Settings;
using Celbridge.WebHost;

namespace Celbridge.UserInterface.Services;

public class UserInterfaceService : IUserInterfaceService
{
    // Below this the rasterization scale has not meaningfully moved, so the published value stands.
    private const double RasterizationScaleTolerance = 0.0001;

    private readonly ILogger<UserInterfaceService> _logger;
    private IMessengerService _messengerService;
    private ISettingsService _settingsService;
    private IWebViewStateService _webViewStateService;
    private readonly IPlatformInfo _platformInfo;
    private readonly IWindowActivationMonitor _windowActivationMonitor;

    private Window? _mainWindow;
    private XamlRoot? _xamlRoot;
    private ThemeHelper? _themeHelper;
    private Helpers.WindowStateHelper? _windowStateHelper;
    private double _publishedRasterizationScale;

    public object MainWindow => _mainWindow!;
    public object XamlRoot => _xamlRoot!;

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

        _windowActivationMonitor.Start(_mainWindow);

        ApplyCurrentTheme();

        PublishRasterizationScale();
        _xamlRoot.Changed += XamlRoot_Changed;

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

        if (_platformInfo.RequiresMacOSKeyCommandRouting)
        {
            var routerInstalled = Platform.MacOSKeyCommandRouter.Install();
            if (!routerInstalled)
            {
                _logger.LogWarning("Failed to install the editing key command router");
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

    private void XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        PublishRasterizationScale();
    }

    // A hosted web view renders at a rasterization scale that folds in the Windows accessibility text scale,
    // so a CSS pixel there is larger than a device-independent pixel. Publishing the host's own scale lets a
    // client derive the difference and divide it out of the dimensions that mirror native chrome
    // (see core/state-store.js). XamlRoot.Changed also fires for size and visibility, so only a new value is
    // broadcast, keeping a window resize off the state channel.
    private void PublishRasterizationScale()
    {
        Guard.IsNotNull(_xamlRoot);

        var rasterizationScale = _xamlRoot.RasterizationScale;
        if (double.IsNaN(rasterizationScale) ||
            rasterizationScale <= 0)
        {
            return;
        }

        if (Math.Abs(rasterizationScale - _publishedRasterizationScale) < RasterizationScaleTolerance)
        {
            return;
        }

        _publishedRasterizationScale = rasterizationScale;
        _webViewStateService.AppState.SetValue(
            "rasterizationScale",
            rasterizationScale.ToString(CultureInfo.InvariantCulture));
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

    public void SetTheme(ApplicationColorTheme theme)
    {
        _settingsService.Set(SettingCatalog.Application.Theme, theme);
        ApplyCurrentTheme();
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
        // this up in their connect snapshot. Open ones receive the broadcast.
        _webViewStateService.AppState.SetValue("theme", UserInterfaceTheme.ToString());

        // Update titlebar buttons
        _themeHelper?.UpdateTitleBar(UserInterfaceTheme);
    }
}
