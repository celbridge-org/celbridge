using Celbridge.Logging;
using Windows.UI.ViewManagement;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Detects and applies system theme changes. The system theme listener (UISettings.ColorValuesChanged)
/// runs on both the packaged WinUI head and the Skia desktop head. The caption-button theming in
/// UpdateTitleBar applies only to the integrated WinUI title bar.
/// </summary>
public class ThemeHelper
{
    private readonly Window? _mainWindow;

    private UISettings? _uiSettings;
    private Action<UserInterfaceTheme>? _onThemeChanged;

    public ThemeHelper(Window mainWindow)
    {
        _mainWindow = mainWindow;
    }

    public void Initialize(Action<UserInterfaceTheme> onThemeChanged)
    {
        _onThemeChanged = onThemeChanged;

        // Listen for system theme changes via UISettings. This is supported on both Windows heads.
        // Wrapped defensively so a platform that does not support UISettings degrades to no live theme
        // updates rather than failing startup (relevant to the future macOS desktop head).
        try
        {
            _uiSettings = new UISettings();
            _uiSettings.ColorValuesChanged += UISettings_ColorValuesChanged;
        }
        catch (Exception ex)
        {
            var logger = ServiceLocator.AcquireService<ILogger<ThemeHelper>>();
            logger.LogDebug(ex, "System theme change listener is not available on this platform");
        }
    }

    private void UISettings_ColorValuesChanged(UISettings sender, object args)
    {
        // UISettings events fire on a background thread, so dispatch to UI thread
        _mainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            // Get the current OS theme
            var osTheme = SystemThemeHelper.GetCurrentOsTheme();
            var currentTheme = osTheme == ApplicationTheme.Dark 
                ? UserInterfaceTheme.Dark 
                : UserInterfaceTheme.Light;

            // Notify the callback
            _onThemeChanged?.Invoke(currentTheme);
        });
    }

    public void UpdateTitleBar(UserInterfaceTheme theme)
    {
#if WINDOWS
        if (_mainWindow?.AppWindow?.TitleBar == null)
        {
            return;
        }

        var titleBar = _mainWindow.AppWindow.TitleBar;
        var backgroundColor = GetTitleBarColor(theme);

        // Explicitly set all button colors based on the current theme.
        // I tried several approaches using the built-in color switching behaviour, but there
        // were lots of edge cases that didn't work correctly. In the ended up just setting all the
        // colors explicitly, which is hacky but works reliably across all themes and system settings.
        if (theme == UserInterfaceTheme.Dark)
        {
            titleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
            titleBar.ButtonBackgroundColor = backgroundColor;
            titleBar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
            titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(255, 45, 45, 45);
            titleBar.ButtonPressedForegroundColor = Windows.UI.Color.FromArgb(255, 200, 200, 200);
            titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(255, 60, 60, 60);
            titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 128, 128, 128);
            titleBar.ButtonInactiveBackgroundColor = backgroundColor;
        }
        else
        {
            titleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 0, 0, 0);
            titleBar.ButtonBackgroundColor = backgroundColor;
            titleBar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(255, 0, 0, 0);
            titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(255, 230, 230, 230);
            titleBar.ButtonPressedForegroundColor = Windows.UI.Color.FromArgb(255, 60, 60, 60);
            titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(255, 200, 200, 200);
            titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 128, 128, 128);
            titleBar.ButtonInactiveBackgroundColor = backgroundColor;
        }

#else
        // No-op on non-Windows platforms
#endif
    }

#if WINDOWS
    /// <summary>
    /// Reads the TitleBarActiveColor from the theme resources defined in Colors.xaml.
    /// </summary>
    private static Windows.UI.Color GetTitleBarColor(UserInterfaceTheme theme)
    {
        var themeKey = theme == UserInterfaceTheme.Dark ? "Dark" : "Light";
        var themeDictionaries = Application.Current.Resources.ThemeDictionaries;

        if (themeDictionaries.TryGetValue(themeKey, out var dict) &&
            dict is ResourceDictionary themeDict &&
            themeDict.TryGetValue("TitleBarActiveColor", out var colorObj) &&
            colorObj is Windows.UI.Color color)
        {
            return color;
        }

        // Fallback to transparent if the resource isn't found
        return Windows.UI.Color.FromArgb(0, 0, 0, 0);
    }
#endif
}
