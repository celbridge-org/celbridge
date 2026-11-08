// Compiled only under WINDOWS, so the Skia build never links against the WinAppSDK WebView2 surface. The DI
// selection is gated on the same symbol.
#if WINDOWS
using System.Text.Json;
using Celbridge.Automation;
using Celbridge.Logging;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost.Platform;

/// <summary>
/// A web view on the packaged Windows head, which drives the WebView2 SDK directly.
/// </summary>
internal sealed class WindowsWebView : WebViewBase
{
    // Bounds the wait for Page.captureScreenshot. WinUI pauses the WebView2 renderer of a tab that is out of
    // view, and the CDP call then never returns.
    private static readonly TimeSpan ScreenshotCaptureTimeout = TimeSpan.FromSeconds(5);

    private readonly Microsoft.UI.Xaml.Input.KeyEventHandler _controlKeyDownHandler;

    // Whether a key has reached the control since the control last took focus. Once one key goes astray, the keys
    // after it go astray too, so one log line per focus is enough.
    private bool _isKeyOnControlReported;

    // The icon address at WebView2's last FaviconChanged event.
    private string _changedFaviconUri = string.Empty;

    // The icon address of the last copy written to the store.
    private string _storedFaviconUri = string.Empty;

    public WindowsWebView(WebView2 control, WindowsWebViewPlatform platform, ILogger logger, IAutomationService automationService)
        : base(control, platform, logger, automationService)
    {
        // On this head, an ordinary key raises the control's own key events only when it went to the host instead of
        // the page. The handler is registered with handledEventsToo, so it also sees keys that another handler marked
        // handled.
        _controlKeyDownHandler = Control_KeyDown;
        control.AddHandler(UIElement.KeyDownEvent, _controlKeyDownHandler, handledEventsToo: true);
        control.GotFocus += Control_GotFocus;

        LiveCoreWebView2.WindowCloseRequested += CoreWebView2_WindowCloseRequested;
        LiveCoreWebView2.FaviconChanged += CoreWebView2_FaviconChanged;
        NavigationCompleted += WindowsWebView_NavigationCompleted;
    }

    private void CoreWebView2_FaviconChanged(CoreWebView2 sender, object args)
    {
        _changedFaviconUri = LiveCoreWebView2.FaviconUri ?? string.Empty;

        RaiseFaviconChanged();
    }

    // WebView2 raises FaviconChanged only when the icon's address changes. A new page can keep the previous page's icon
    // address. This handler raises the event for that page, so every loaded page reports its icon.
    private void WindowsWebView_NavigationCompleted(object? sender, WebNavigationCompletedEventArgs e)
    {
        var address = LiveCoreWebView2.FaviconUri ?? string.Empty;
        if (e.Result != WebNavigationResult.Succeeded
            || address.Length == 0
            || address != _changedFaviconUri)
        {
            return;
        }

        RaiseFaviconChanged();
    }

    // WebView2 fetches and caches the icon. The store keeps a copy, and a restored tab shows the copy before its page
    // loads. This method writes a new copy when the icon's address changes.
    public override async Task<WebViewFavicon?> ReadFaviconAsync(IWebViewFaviconStore store)
    {
        var address = LiveCoreWebView2.FaviconUri;
        if (string.IsNullOrEmpty(address))
        {
            return null;
        }

        using var stream = await LiveCoreWebView2.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
        if (stream is null
            || stream.Size == 0)
        {
            return null;
        }

        using var memoryStream = new MemoryStream();
        await stream.AsStreamForRead().CopyToAsync(memoryStream);

        var image = memoryStream.ToArray();
        if (address != _storedFaviconUri)
        {
            _storedFaviconUri = address;
            await store.WriteIconAsync(address, image);
        }

        return new WebViewFavicon(address, image);
    }

    private void CoreWebView2_WindowCloseRequested(CoreWebView2 sender, object args)
    {
        RaisePageClosed();
    }

    // The owner answers through the request, and the request's deferral pauses the page's window.open until then. With
    // no owner listening, the request is declined.
    protected override void OnPlatformNewWindowRequested(CoreWebView2NewWindowRequestedEventArgs args)
    {
        if (!HasNewWindowHandler)
        {
            args.Handled = true;
            return;
        }

        RaiseNewWindowRequested(new WindowsNewWindowRequest(args));
    }

    private void Control_GotFocus(object sender, RoutedEventArgs e)
    {
        _isKeyOnControlReported = false;
    }

    private void Control_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (_isKeyOnControlReported ||
            IsAcceleratorKey(e.Key))
        {
            return;
        }

        _isKeyOnControlReported = true;
        Logger.LogDebug(
            "A key reached the control of the web view for {Resource} rather than its page, with the control's focus state {FocusState}",
            Resource,
            Control?.FocusState);
    }

    // WebView2 passes an accelerator key to the host as well as the page. An accelerator key is a key pressed with
    // Control or Alt, Escape, or a function key. An accelerator key reaching the control says nothing about where the
    // keyboard is.
    private static bool IsAcceleratorKey(Windows.System.VirtualKey key)
    {
        return IsKeyDown(Windows.System.VirtualKey.Control)
            || IsKeyDown(Windows.System.VirtualKey.Menu)
            || key == Windows.System.VirtualKey.Escape
            || (key >= Windows.System.VirtualKey.F1 && key <= Windows.System.VirtualKey.F24);
    }

    private static bool IsKeyDown(Windows.System.VirtualKey key)
    {
        return Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
    }

    protected override void ReleaseResources()
    {
        base.ReleaseResources();

        LiveCoreWebView2.WindowCloseRequested -= CoreWebView2_WindowCloseRequested;
        LiveCoreWebView2.FaviconChanged -= CoreWebView2_FaviconChanged;
        NavigationCompleted -= WindowsWebView_NavigationCompleted;

        if (Control is not null)
        {
            Control.RemoveHandler(UIElement.KeyDownEvent, _controlKeyDownHandler);
            Control.GotFocus -= Control_GotFocus;
        }
    }

    protected override void ApplyOptions(WebViewOptions options)
    {
        LiveCoreWebView2.Settings.AreDevToolsEnabled = options.AreDevToolsEnabled;
        LiveCoreWebView2.Settings.IsZoomControlEnabled = options.IsZoomEnabled;

        // Sites already recognise the WebView2 User-Agent, so the application token is appended to it. The options
        // are applied once, so the token is never appended twice.
        if (!string.IsNullOrEmpty(options.UserAgentToken))
        {
            LiveCoreWebView2.Settings.UserAgent = $"{LiveCoreWebView2.Settings.UserAgent} {options.UserAgentToken}";
        }
    }

    // This head maps a virtual host to a real https origin instead, so nothing loads an HTML string here.
    protected override void LoadHtmlStringCore(string html, string baseUrl)
    {
        throw new NotSupportedException("Loading an HTML string with a base URL is not supported on this platform.");
    }

    public override async Task StopAsync()
    {
        await Task.CompletedTask;

        LiveCoreWebView2.Stop();
    }

    protected override async Task ReloadPageAsync(bool clearCache)
    {
        if (clearCache)
        {
            await LiveCoreWebView2.Profile.ClearBrowsingDataAsync(
                CoreWebView2BrowsingDataKinds.CacheStorage | CoreWebView2BrowsingDataKinds.DiskCache);
        }

        LiveCoreWebView2.Reload();
    }

    // Chromium's built-in find bar serves this head, so the host never drives find here.
    public override async Task StartFindAsync(string term, FindOptions options)
    {
        await Task.CompletedTask;
    }

    public override void FindNext()
    {
    }

    public override void FindPrevious()
    {
    }

    public override void StopFind()
    {
    }

    protected override async Task<bool> InstallDocumentStartScriptAsync(string script)
    {
        await LiveCoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);

        return true;
    }

    // XAML arranges the packaged WebView2, so the page's viewport already follows the control. Before XAML arranges
    // the control, the control has no geometry to give.
    protected override bool SetNativeViewportSize(double width, double height)
    {
        return false;
    }

    // Runs through the DevTools protocol, so the host's script doesn't change the page's user activation.
    protected override async Task<string> EvalScriptAsync(string expression)
    {
        var paramsJson = DevToolsEvaluation.BuildEvaluateParams(expression);
        var responseJson = await LiveCoreWebView2
            .CallDevToolsProtocolMethodAsync(DevToolsEvaluation.MethodName, paramsJson);

        return DevToolsEvaluation.ReadEvaluateResult(responseJson);
    }

    protected override async Task<ScreenshotData> CaptureScreenshotCoreAsync(ScreenshotRequest request)
    {
        var paramsJson = BuildCaptureScreenshotParams(request);
        var captureTask = LiveCoreWebView2
            .CallDevToolsProtocolMethodAsync("Page.captureScreenshot", paramsJson)
            .AsTask();

        // Bounded wait, so a tab switch mid-capture surfaces as a timeout.
        var winner = await Task.WhenAny(captureTask, Task.Delay(ScreenshotCaptureTimeout));
        if (winner != captureTask)
        {
            throw new TimeoutException(
                $"Screenshot timed out after {ScreenshotCaptureTimeout.TotalSeconds:0}s. " +
                "The document tab likely went off screen during capture, which pauses " +
                "WebView2 rendering. Bring the tab to the front and retry.");
        }

        var resultJson = await captureTask;
        using var doc = JsonDocument.Parse(resultJson);
        var base64 = doc.RootElement.GetProperty("data").GetString() ?? string.Empty;
        // Decode at the platform boundary so downstream stages carry raw bytes. JSON envelopes can otherwise
        // escape '+' and corrupt the payload.
        var bytes = Convert.FromBase64String(base64);

        int width;
        int height;
        if (request.Clip is not null)
        {
            width = (int)Math.Round(request.Clip.Width * request.Clip.Scale);
            height = (int)Math.Round(request.Clip.Height * request.Clip.Scale);
        }
        else
        {
            width = 0;
            height = 0;
        }

        return new ScreenshotData(request.Format, width, height, bytes);
    }

    protected override IWebViewDownloadHandler CreateDownloadHandler()
    {
        return WebView2DownloadHandler.Attach(LiveCoreWebView2);
    }

    // WebView2 changes Source as a navigation commits.
    protected override IDisposable ObserveNavigationCommits(NavigationCommitted onCommitted)
    {
        return new SourceChangedObserver(LiveCoreWebView2, onCommitted);
    }

    /// <summary>
    /// A new window request that WebView2 leaves waiting until the deferral completes. Giving the request a view's
    /// CoreWebView2 links the new page to the opener. That view must not have navigated yet.
    /// </summary>
    private sealed class WindowsNewWindowRequest : WebNewWindowRequest
    {
        private readonly CoreWebView2NewWindowRequestedEventArgs _args;
        private readonly Windows.Foundation.Deferral _deferral;
        private bool _isAnswered;

        public WindowsNewWindowRequest(CoreWebView2NewWindowRequestedEventArgs args)
            : base(args.Uri ?? string.Empty, args.IsUserInitiated)
        {
            _args = args;
            _deferral = args.GetDeferral();
        }

        public override Task AcceptAsync(IEditorWebView webView)
        {
            if (_isAnswered)
            {
                return Task.CompletedTask;
            }

            // The request counts as answered only once the view is set. A setter that throws leaves Decline able to
            // complete the request.
            _args.NewWindow = ((WindowsWebView)webView).LiveCoreWebView2;
            _isAnswered = true;
            _args.Handled = true;
            _deferral.Complete();

            return Task.CompletedTask;
        }

        public override void Decline()
        {
            if (_isAnswered)
            {
                return;
            }

            _isAnswered = true;
            _args.Handled = true;
            _deferral.Complete();
        }
    }

    private static string BuildCaptureScreenshotParams(ScreenshotRequest request)
    {
        var payload = new Dictionary<string, object>
        {
            ["format"] = request.Format
        };
        if (request.Format == "jpeg")
        {
            payload["quality"] = request.Quality;
        }
        if (request.Clip is not null)
        {
            payload["clip"] = new Dictionary<string, object>
            {
                ["x"] = request.Clip.X,
                ["y"] = request.Clip.Y,
                ["width"] = request.Clip.Width,
                ["height"] = request.Clip.Height,
                ["scale"] = request.Clip.Scale
            };
        }
        return JsonSerializer.Serialize(payload);
    }
}
#endif
