// Compiled only under WINDOWS, so the Skia build never links against the WinAppSDK WebView2 surface. The DI
// selection is gated on the same symbol.
#if WINDOWS
using System.Text.Json;
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

    public WindowsWebView(WebView2 control, WindowsWebViewPlatform platform, ILogger logger)
        : base(control, platform, logger)
    {
        // On this head, an ordinary key raises the control's own key events only when it went to the host instead of
        // the page. The handler is registered with handledEventsToo, so it also sees keys that another handler marked
        // handled.
        _controlKeyDownHandler = Control_KeyDown;
        control.AddHandler(UIElement.KeyDownEvent, _controlKeyDownHandler, handledEventsToo: true);
        control.GotFocus += Control_GotFocus;
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

        if (Control is not null)
        {
            Control.RemoveHandler(UIElement.KeyDownEvent, _controlKeyDownHandler);
            Control.GotFocus -= Control_GotFocus;
        }
    }

    protected override void ApplyOptions(WebViewOptions options)
    {
        LiveCoreWebView2.Settings.AreDevToolsEnabled = options.IsDevToolsEnabled;
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

    protected override async Task<string> EvalScriptAsync(string expression)
    {
        return await LiveCoreWebView2.ExecuteScriptAsync(expression);
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
