#if WINDOWS
using Celbridge.Logging;
using Celbridge.UserInterface.Views;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Windows title-bar chrome wrapper. Hosts the platform-neutral ApplicationToolbar inside the custom
/// title bar (MainPage extends content into the title bar and assigns this control as the drag region)
/// and carves out interactive passthrough regions so the toolbar's buttons receive clicks instead of
/// the window-drag chrome. Used only on Windows. The Skia desktop heads host the ApplicationToolbar
/// directly beneath the native title bar.
/// </summary>
public sealed class TitleBar : UserControl, ITitleBar
{
    private readonly ApplicationToolbar _applicationToolbar;
    private Window? _mainWindow;

    public TitleBar()
    {
        // The toolbar reserves its own trailing column for the caption buttons so its background paints
        // behind them. No outer margin is needed here.
        _applicationToolbar = new ApplicationToolbar();

        Content = _applicationToolbar;

        Loaded += OnTitleBar_Loaded;
        Unloaded += OnTitleBar_Unloaded;
    }

    public bool BuildShortcutButtons(IReadOnlyList<Shortcut> shortcuts, Action<string> onScriptExecute)
    {
        return _applicationToolbar.BuildShortcutButtons(shortcuts, onScriptExecute);
    }

    public void SetShortcutButtonsVisible(bool isVisible)
    {
        _applicationToolbar.SetShortcutButtonsVisible(isVisible);
    }

    public void ClearShortcutButtons()
    {
        _applicationToolbar.ClearShortcutButtons();
    }

    public bool BuildUtilityButtons(IReadOnlyList<UtilityButton> utilities, Action<string> onOpenUtility)
    {
        return _applicationToolbar.BuildUtilityButtons(utilities, onOpenUtility);
    }

    public void ClearUtilityButtons()
    {
        _applicationToolbar.ClearUtilityButtons();
    }

    private void OnTitleBar_Loaded(object sender, RoutedEventArgs e)
    {
        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        _mainWindow = userInterfaceService.MainWindow as Window;

        _applicationToolbar.InteractiveLayoutChanged += OnInteractiveLayoutChanged;

        UpdateInteractiveRegions();
    }

    private void OnTitleBar_Unloaded(object sender, RoutedEventArgs e)
    {
        _applicationToolbar.InteractiveLayoutChanged -= OnInteractiveLayoutChanged;

        Loaded -= OnTitleBar_Loaded;
        Unloaded -= OnTitleBar_Unloaded;
    }

    private void OnInteractiveLayoutChanged(object? sender, EventArgs e)
    {
        UpdateInteractiveRegions();
    }

    private void UpdateInteractiveRegions()
    {
        try
        {
            if (_mainWindow is null)
            {
                return;
            }

            var appWindow = _mainWindow.AppWindow;
            if (appWindow is null)
            {
                return;
            }

            var nonClientInputSource = Microsoft.UI.Input.InputNonClientPointerSource.GetForWindowId(appWindow.Id);
            var scale = _mainWindow.Content.XamlRoot?.RasterizationScale ?? 1.0;
            var rootContent = _mainWindow.Content;

            var regions = new List<Windows.Graphics.RectInt32>();
            foreach (var element in _applicationToolbar.GetPassthroughElements())
            {
                var transform = element.TransformToVisual(rootContent);
                var position = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

                var region = new Windows.Graphics.RectInt32(
                    (int)(position.X * scale),
                    (int)(position.Y * scale),
                    (int)(element.ActualWidth * scale),
                    (int)(element.ActualHeight * scale));
                regions.Add(region);
            }

            if (regions.Count > 0)
            {
                nonClientInputSource.SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.Passthrough, regions.ToArray());
            }
            else
            {
                nonClientInputSource.ClearRegionRects(Microsoft.UI.Input.NonClientRegionKind.Passthrough);
            }
        }
        catch (Exception ex)
        {
            // Best-effort region computation. Log at debug so an unexpected failure is not hidden.
            ServiceLocator.AcquireService<ILogger<TitleBar>>().LogDebug(ex, "Failed to update title bar interactive regions");
        }
    }
}
#endif
