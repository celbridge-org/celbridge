// Compiled only under WINDOWS, as the DI selection is, so the Skia build never links against the WinAppSDK
// WebView2 surface.
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
    // Bounds the wait for Page.captureScreenshot. A WinUI tab that is not being shown pauses the WebView2
    // renderer, which would otherwise leave the CDP call hanging.
    private static readonly TimeSpan ScreenshotCaptureTimeout = TimeSpan.FromSeconds(5);

    private readonly CoreWebView2 _coreWebView2;

    public WindowsWebView(WebView2 control, WindowsWebViewPlatform platform, ILogger logger)
        : base(control, platform, logger)
    {
        _coreWebView2 = CoreWebView2!;
    }

    protected override void ApplyOptions(WebViewOptions options)
    {
        _coreWebView2.Settings.AreDevToolsEnabled = options.IsDevToolsEnabled;
        _coreWebView2.Settings.IsZoomControlEnabled = options.IsZoomEnabled;

        // Sites already recognise the WebView2 User-Agent, so the application token is appended to it rather than
        // replacing it. The options are applied once, so the token is never appended twice.
        if (!string.IsNullOrEmpty(options.UserAgentToken))
        {
            _coreWebView2.Settings.UserAgent = $"{_coreWebView2.Settings.UserAgent} {options.UserAgentToken}";
        }
    }

    // This head maps a virtual host to a real https origin instead, so nothing loads an HTML string here.
    public override void LoadHtmlString(string html, string baseUrl)
    {
        throw new NotSupportedException("Loading an HTML string with a base URL is not supported on this platform.");
    }

    public override async Task StopAsync()
    {
        await Task.CompletedTask;

        _coreWebView2.Stop();
    }

    protected override async Task ReloadPageAsync(bool clearCache)
    {
        if (clearCache)
        {
            await _coreWebView2.Profile.ClearBrowsingDataAsync(
                CoreWebView2BrowsingDataKinds.CacheStorage | CoreWebView2BrowsingDataKinds.DiskCache);
        }

        _coreWebView2.Reload();
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
        await _coreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);

        return true;
    }

    // XAML arranges the packaged WebView2, so its page's viewport already follows the control, and there is no
    // geometry to give a control XAML has not arranged.
    protected override bool SetNativeViewportSize(double width, double height)
    {
        return false;
    }

    protected override async Task<string> EvalScriptAsync(string expression)
    {
        return await _coreWebView2.ExecuteScriptAsync(expression);
    }

    protected override async Task<ScreenshotData> CaptureScreenshotCoreAsync(ScreenshotRequest request)
    {
        var paramsJson = BuildCaptureScreenshotParams(request);
        var captureTask = _coreWebView2
            .CallDevToolsProtocolMethodAsync("Page.captureScreenshot", paramsJson)
            .AsTask();

        // Bounded wait so a tab switch mid-capture surfaces as a timeout instead of an indefinite hang.
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
        return WebView2DownloadHandler.Attach(_coreWebView2);
    }

    // WebView2 changes Source as a navigation commits.
    protected override IDisposable ObserveNavigationCommits(NavigationCommitted onCommitted)
    {
        return new SourceChangedObserver(_coreWebView2, onCommitted);
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
