using Celbridge.Downloads;
using Celbridge.Localization;
using Celbridge.Logging;
using Celbridge.UserInterface;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost.Platform;

/// <summary>
/// The web view platform of the Uno Skia heads. It creates a MacOSWebView on macOS and a SkiaWebView on the
/// other Skia heads, and answers its capabilities by OS.
/// </summary>
internal sealed class SkiaWebViewPlatform : IWebViewPlatform
{
    // Used until the window can supply a size, and as the floor for a window too small to lay a page out in.
    // A page that loads at this size is corrected by the arrange that follows when its surface is shown.
    private const double MinimumViewportWidth = 1024;
    private const double MinimumViewportHeight = 768;

    // Used only when the installed Safari version cannot be read. Kept comfortably above Gmail's minimum so the
    // UA still passes. The real version is preferred whenever available.
    private const string FallbackSafariVersion = "18.0";

    private readonly ILogger<SkiaWebViewPlatform> _logger;
    private readonly ILogger<SkiaWebView> _skiaWebViewLogger;
    private readonly ILogger<MacOSWebView> _macOSWebViewLogger;
    private readonly ILogger<MacOSWebViewDownloadRouter> _downloadRouterLogger;
    private readonly IUserInterfaceService _userInterfaceService;
    private readonly ILocalizerService _localizerService;
    private readonly IDownloadService _downloadService;

    // Hidden, window-rooted host used to initialize WebView2 controls, where EnsureCoreWebView2Async never
    // completes for a control that has not been parented to a window.
    private Panel? _initHost;

    private bool _checkedBackgroundActivity;
    private bool _checkedInactiveSelection;
    private bool _checkedLoadingWhenDetached;
    private bool _reportedRemoteInspection;
    private string? _safariVersion;

    // Routes every web view's downloads on macOS, created with the first view that routes its downloads.
    private MacOSWebViewDownloadRouter? _downloadRouter;

    public SkiaWebViewPlatform(
        ILogger<SkiaWebViewPlatform> logger,
        ILogger<SkiaWebView> skiaWebViewLogger,
        ILogger<MacOSWebView> macOSWebViewLogger,
        ILogger<MacOSWebViewDownloadRouter> downloadRouterLogger,
        IUserInterfaceService userInterfaceService,
        ILocalizerService localizerService,
        IDownloadService downloadService)
    {
        _logger = logger;
        _skiaWebViewLogger = skiaWebViewLogger;
        _macOSWebViewLogger = macOSWebViewLogger;
        _downloadRouterLogger = downloadRouterLogger;
        _userInterfaceService = userInterfaceService;
        _localizerService = localizerService;
        _downloadService = downloadService;
    }

    // Windows-under-Skia hosts a real WebView2 that implements virtual-host mapping. macOS WKWebView and the
    // Linux Skia head do not, and use loadHTMLString instead.
    public bool SupportsVirtualHostMapping => OperatingSystem.IsWindows();

    // Windows-under-Skia hosts Chromium's WebView2 with its own find bar. The macOS WKWebView and Linux
    // WebKitGTK backends have none, so the host find bar drives find through the view there.
    public bool ProvidesBuiltInFind => OperatingSystem.IsWindows();

    public bool CanSizeUnarrangedViewport => OperatingSystem.IsMacOS();

    // CoreWebView2.Profile is unimplemented on every Skia head. macOS clears through the native
    // WKWebsiteDataStore instead. The Windows and Linux Skia heads have no such path.
    public bool SupportsLiveBrowsingDataClear => OperatingSystem.IsMacOS();

    // Only macOS hit-tests each press against the native web views.
    public bool IsLastPressInWebView => OperatingSystem.IsMacOS() && MacOSWebViewFocusMonitor.IsLastPressInWebView;

    public async Task<WebViewBase> CreateWebViewAsync()
    {
        // A transparent background stops the view showing white for a moment when its tab is switched to. Similar
        // issue described here: https://github.com/MicrosoftEdge/WebView2Feedback/issues/1412
        var control = new WebView2
        {
            DefaultBackgroundColor = Colors.Transparent
        };

        var nativeHandle = await InitializeAsync(control);

        // Created after initialization, so the view never sees the hidden host it was initialized in.
        if (OperatingSystem.IsMacOS())
        {
            return new MacOSWebView(control, nativeHandle, this, _macOSWebViewLogger);
        }

        return new SkiaWebView(control, this, _skiaWebViewLogger);
    }

    // EnsureCoreWebView2Async never completes for a control that is not parented to a window. The control is
    // parented in the hidden, window-rooted host for the duration of initialization, then detached so the owner
    // can place it in its own container with the CoreWebView2 already live. Returns the pinned native view on
    // macOS, or zero.
    private async Task<IntPtr> InitializeAsync(WebView2 control)
    {
        var host = await EnsureInitHostAsync();
        host.Children.Add(control);
        try
        {
            if (!control.IsLoaded)
            {
                var loadedCompletionSource = new TaskCompletionSource();
                RoutedEventHandler? onLoaded = null;
                onLoaded = (sender, args) =>
                {
                    control.Loaded -= onLoaded;
                    loadedCompletionSource.TrySetResult();
                };
                control.Loaded += onLoaded;
                await loadedCompletionSource.Task;
            }

            await control.EnsureCoreWebView2Async();

            if (OperatingSystem.IsMacOS() &&
                control.CoreWebView2 is not null)
            {
                return PrepareNativeWebView(control, control.CoreWebView2);
            }

            return IntPtr.Zero;
        }
        finally
        {
            host.Children.Remove(control);
        }
    }

    // Runs before the control leaves the init host. Leaving it is the control's first Unloaded, and Uno disposes
    // the native view on every Unloaded. Returns the pinned native view, or zero when it did not resolve.
    private IntPtr PrepareNativeWebView(WebView2 control, CoreWebView2 coreWebView2)
    {
        // Key forwarding checks which key a web view last received, and a click in a web view is its focus
        // signal, so both are observed before any web view can take input.
        MacOSWebViewInterop.ObserveKeyDownDelivery();
        MacOSWebViewFocusMonitor.Install(_logger);

        // Pin the native WKWebView for the process lifetime and keep it schedulable while hidden. Uno's native
        // element disposes the view on every Unloaded and later touches the stale handle, which is a
        // use-after-free.
        var nativeWebViewHandle = IntPtr.Zero;
        if (MacOSWebViewInterop.TryGetNativeWebViewHandle(coreWebView2, out var resolvedHandle, out var detail))
        {
            nativeWebViewHandle = resolvedHandle;
            PinNativeWebView(nativeWebViewHandle);
            KeepSelectionWhileUnfocused(nativeWebViewHandle);
            ApplyInitialViewportSize(nativeWebViewHandle);
        }
        else
        {
            _logger.LogDebug("Native WKWebView handle not resolvable after init ({Detail}); pinning deferred to first resolution", detail);
        }

        KeepLoadingWhenDetached();

        // UNO-BUG: the script message handler is registered on every Loaded and never removed. The second load
        // of a control then aborts the process inside WebKit. This control sees a second load as soon as it
        // leaves the init host for its real container, so drop the handler on every Unloaded and let Uno's next
        // Loaded register it again.
        control.Unloaded -= Control_Unloaded;
        control.Unloaded += Control_Unloaded;

        return nativeWebViewHandle;
    }

    private void Control_Unloaded(object sender, RoutedEventArgs e)
    {
        var control = sender as WebView2;
        if (control?.CoreWebView2 is null)
        {
            return;
        }

        if (MacOSWebViewInterop.TryGetNativeWebViewHandle(control.CoreWebView2, out var nativeWebViewHandle, out var detail))
        {
            MacOSWebViewInterop.RemoveUnoScriptMessageHandler(nativeWebViewHandle);
        }
        else
        {
            _logger.LogWarning("Could not remove the WebView script message handler: {Detail}", detail);
        }
    }

    // UNO-BUG: the native frame is arranged only while the control is in the visual tree.
    // A surface that loads while it is not (a document restored into a background tab, a utility running
    // from project load) reports a zero-sized window to its page: layout collapses, and a page that derives
    // geometry from the viewport at startup divides by zero and stays broken even after the real arrange
    // arrives. The placeholder is the size of the window, so a page cannot tell it from a real layout.
    private void ApplyInitialViewportSize(IntPtr nativeWebViewHandle)
    {
        double width = MinimumViewportWidth;
        double height = MinimumViewportHeight;

        if (_userInterfaceService.MainWindow is Window mainWindow
            && mainWindow.Content is FrameworkElement windowContent)
        {
            width = Math.Max(windowContent.ActualWidth, MinimumViewportWidth);
            height = Math.Max(windowContent.ActualHeight, MinimumViewportHeight);
        }

        MacOSWebViewInterop.SetViewportSize(nativeWebViewHandle, width, height);
    }

    /// <summary>
    /// Pins a native WKWebView for the process lifetime and keeps its page running while it is hidden. WebKit
    /// suspends a hidden page's process, which stalls host-to-editor RPC for a background document tab until the
    /// tab is shown again. Each native view is pinned once, when it first resolves.
    /// </summary>
    internal void PinNativeWebView(IntPtr nativeWebViewHandle)
    {
        var applied = MacOSWebViewInterop.RetainNativeWebView(nativeWebViewHandle);

        if (applied.Count < MacOSWebViewInterop.BackgroundPageActivityPreferenceCount)
        {
            _logger.LogWarning(
                "WebKit did not accept every background page activity preference for this web view, so its document may stop servicing host RPC while it is a background tab. Applied: {Applied}",
                applied.Count == 0 ? "none" : string.Join(", ", applied));
            return;
        }

        if (_checkedBackgroundActivity)
        {
            return;
        }

        _checkedBackgroundActivity = true;

        _logger.LogDebug("Background page activity preferences applied: {Applied}", string.Join(", ", applied));
    }

    // Managed focus moves resign the web view's first responder status, and WebKit discards the page's
    // selection when that happens, so a selection the user just made in a hosted page disappears. Reported
    // once per session because losing the SPI brings the disappearing selection back.
    private void KeepSelectionWhileUnfocused(IntPtr nativeWebViewHandle)
    {
        var maintained = MacOSWebViewInterop.MaintainInactiveSelection(nativeWebViewHandle);

        if (_checkedInactiveSelection)
        {
            return;
        }

        _checkedInactiveSelection = true;

        if (maintained)
        {
            _logger.LogDebug("Hosted pages keep their selection while unfocused");
            return;
        }

        _logger.LogWarning(
            "WebKit no longer exposes the inactive selection setting, so a selection in a hosted page is lost when focus moves");
    }

    // A web view leaves the visual tree whenever its document goes to a background tab, and without this a
    // page still loading at that moment never finishes. Installed once, before the first web view leaves the
    // host it was initialized in.
    private void KeepLoadingWhenDetached()
    {
        if (_checkedLoadingWhenDetached)
        {
            return;
        }

        _checkedLoadingWhenDetached = true;

        if (!MacOSWebViewInterop.KeepLoadingWhenDetached())
        {
            _logger.LogWarning(
                "Uno's web view no longer has the dispose method it is hooked on, so a document sent to a background tab while its page loads may stay blank");
        }
    }

    /// <summary>
    /// Reports whether hosted pages can be inspected, for the first web view only. A silent failure here leaves
    /// every hosted page undebuggable, with nothing in the log to say so.
    /// </summary>
    internal void ReportRemoteInspectionOnce(bool enabled, bool applied)
    {
        if (_reportedRemoteInspection)
        {
            return;
        }

        _reportedRemoteInspection = true;

        if (!applied)
        {
            _logger.LogWarning(
                "WebKit did not accept an inspection setting, so hosted pages may not appear in Safari's Develop menu");
            return;
        }

        if (enabled)
        {
            _logger.LogDebug("Hosted pages are inspectable from Safari's Develop menu");
            return;
        }

        _logger.LogDebug("Hosted pages are not inspectable: web inspection is disabled");
    }

    /// <summary>
    /// The installed Safari's version, read once.
    /// </summary>
    internal string SafariVersion => _safariVersion ??= ResolveSafariVersion();

    private string ResolveSafariVersion()
    {
        var version = MacOSWebViewInterop.GetSafariVersion();
        if (string.IsNullOrEmpty(version))
        {
            _logger.LogWarning("Could not read the installed Safari version; falling back to {Fallback}", FallbackSafariVersion);
            return FallbackSafariVersion;
        }

        return version;
    }

    /// <summary>
    /// Routes a macOS web view's downloads through the download service. A zero handle gets a handler that routes
    /// nothing.
    /// </summary>
    internal IWebViewDownloadHandler RouteDownloads(IntPtr nativeWebViewHandle)
    {
        _downloadRouter ??= new MacOSWebViewDownloadRouter(_downloadRouterLogger, _localizerService, _downloadService);

        return _downloadRouter.Attach(nativeWebViewHandle);
    }

    public async Task ClearBrowsingDataAsync()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var cleared = await MacOSWebViewInterop.ClearBrowsingDataAsync();
        if (!cleared)
        {
            throw new InvalidOperationException("The native WKWebsiteDataStore clear did not complete");
        }
    }

    private async Task<Panel> EnsureInitHostAsync()
    {
        if (_initHost is not null)
        {
            return _initHost;
        }

        // The factory prewarms views from application startup, before the window content exists, so wait for the
        // root grid rather than failing the first views.
        var pollInterval = TimeSpan.FromMilliseconds(100);
        var rootGridWait = TimeSpan.Zero;

        Grid? rootGrid = null;
        while (rootGrid is null)
        {
            if (_userInterfaceService.MainWindow is Window mainWindow &&
                mainWindow.Content is Grid windowRootGrid)
            {
                rootGrid = windowRootGrid;
                break;
            }

            if (rootGridWait > TimeSpan.FromSeconds(30))
            {
                throw new InvalidOperationException(
                    "Cannot initialize WebView2: the application root grid did not become available");
            }

            await Task.Delay(pollInterval);
            rootGridWait += pollInterval;
        }

        var host = new Grid
        {
            Width = 1,
            Height = 1,
            Opacity = 0,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        rootGrid.Children.Add(host);
        _initHost = host;

        return host;
    }
}
