#if WINDOWS
using Celbridge.Logging;
using Celbridge.UserInterface.Views;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Windows.Foundation;
using Windows.Graphics;

namespace Celbridge.UserInterface.Platform;

/// <summary>
/// Windows title-bar chrome wrapper. Hosts the platform-neutral ApplicationToolbar inside the custom
/// title bar and carves out interactive passthrough regions so the toolbar's buttons receive clicks
/// instead of the window-drag chrome.
/// </summary>
public sealed class TitleBar : UserControl
{
    private readonly ILogger<TitleBar> _logger;
    private readonly ApplicationToolbar _applicationToolbar;
    private Window? _mainWindow;

    public TitleBar()
    {
        _logger = ServiceLocator.AcquireService<ILogger<TitleBar>>();

        _applicationToolbar = new ApplicationToolbar();

        Content = _applicationToolbar;

        Loaded += OnTitleBar_Loaded;
        Unloaded += OnTitleBar_Unloaded;
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

            var captionButtonsLeftEdge = GetCaptionButtonsLeftEdge(appWindow);
            if (captionButtonsLeftEdge is null)
            {
                // A minimized window reports a placeholder size and a negative inset, so there is nothing to
                // measure against. The regions from the last real layout stay in place.
                return;
            }

            var regions = MeasurePassthroughRegions(captionButtonsLeftEdge.Value);

            var nonClientInputSource = InputNonClientPointerSource.GetForWindowId(appWindow.Id);
            if (regions.Count > 0)
            {
                nonClientInputSource.SetRegionRects(NonClientRegionKind.Passthrough, regions.ToArray());
            }
            else
            {
                nonClientInputSource.ClearRegionRects(NonClientRegionKind.Passthrough);
            }
        }
        catch (Exception exception)
        {
            // Best-effort region computation. Log at debug so an unexpected failure is not hidden.
            _logger.LogDebug(exception, "Failed to update title bar interactive regions");
        }
    }

    private List<RectInt32> MeasurePassthroughRegions(int captionButtonsLeftEdge)
    {
        Guard.IsNotNull(_mainWindow);

        var rootContent = _mainWindow.Content;
        var scale = rootContent.XamlRoot?.RasterizationScale ?? 1.0;

        var regions = new List<RectInt32>();

        foreach (var element in _applicationToolbar.GetPassthroughElements())
        {
            var region = MeasureRegion(element, rootContent, scale);

            // A region is only replaced on the next layout pass, so one that reaches into the caption
            // buttons takes their input until something else moves. Elements measured part way through a
            // layout change are the way that happens, so drop the region rather than trust the bounds.
            if (region.X + region.Width > captionButtonsLeftEdge)
            {
                _logger.LogWarning(
                    "Dropped the title bar passthrough region for {ElementName}, which reaches past the caption button edge at {CaptionButtonsLeftEdge}",
                    element.Name,
                    captionButtonsLeftEdge);
                continue;
            }

            regions.Add(region);
        }

        return regions;
    }

    // The element's bounds in the window's physical pixels, which is what the non-client region API takes.
    private static RectInt32 MeasureRegion(FrameworkElement element, UIElement rootContent, double scale)
    {
        var transform = element.TransformToVisual(rootContent);
        var position = transform.TransformPoint(new Point(0, 0));

        return new RectInt32(
            (int)(position.X * scale),
            (int)(position.Y * scale),
            (int)(element.ActualWidth * scale),
            (int)(element.ActualHeight * scale));
    }

    // Where the system caption buttons start, in the window's physical pixels. Null while the window has no
    // real layout to measure, which the caller treats as nothing to update.
    private static int? GetCaptionButtonsLeftEdge(AppWindow appWindow)
    {
        var inset = appWindow.TitleBar?.RightInset ?? 0;
        if (inset <= 0)
        {
            return null;
        }

        return appWindow.Size.Width - inset;
    }
}
#endif
