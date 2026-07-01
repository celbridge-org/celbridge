using System.Text.Json;
using Celbridge.Logging;
using Celbridge.UserInterface;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost.Platform;

/// <summary>
/// IWebViewAdapter for the Uno Skia heads. Falls back to ExecuteScriptAsync where the managed CoreWebView2
/// surface is unimplemented and, on macOS, to the native WKWebView interop in MacOSWebViewInterop. The macOS
/// fallbacks are runtime-gated, so the same implementation also serves the desktop Windows head.
/// </summary>
public sealed class SkiaWebViewAdapter : IWebViewAdapter
{
    private readonly ILogger<SkiaWebViewAdapter> _logger;

    // Hidden, window-rooted host used to initialize WebView2 controls, where EnsureCoreWebView2Async never
    // completes for a control that has not been parented to a window.
    private Panel? _initHost;

    public SkiaWebViewAdapter(ILogger<SkiaWebViewAdapter> logger)
    {
        _logger = logger;
    }

    // In-place creation and the page unload are both consequences of the macOS Skia WebView2 being a native
    // WKWebView: re-parenting resets its context, and Close() does not destroy it. Both coincide with "not
    // Windows" on the Skia heads (the desktop Windows head re-parents and disposes harmlessly).
    public bool CreatesWebViewInPlace => !OperatingSystem.IsWindows();

    public bool RequiresPageUnloadBeforeClose => !OperatingSystem.IsWindows();

    public bool UsesPrewarmedPool => false;

    // Windows-under-Skia hosts a real WebView2 that implements virtual-host mapping; macOS WKWebView and the
    // Linux Skia head do not, and use loadHTMLString instead.
    public bool SupportsVirtualHostMapping => OperatingSystem.IsWindows();

    public async Task EnsureCoreWebView2Async(WebView2 webView)
    {
        // EnsureCoreWebView2Async never completes for a control that is not parented to a window. Parent the
        // control in the hidden, window-rooted host for the duration of initialization, then detach it so the
        // consumer can place it in its own container with the CoreWebView2 already live.
        var host = EnsureInitHost();
        host.Children.Add(webView);
        try
        {
            if (!webView.IsLoaded)
            {
                var loadedCompletionSource = new TaskCompletionSource();
                RoutedEventHandler? onLoaded = null;
                onLoaded = (sender, args) =>
                {
                    webView.Loaded -= onLoaded;
                    loadedCompletionSource.TrySetResult();
                };
                webView.Loaded += onLoaded;
                await loadedCompletionSource.Task;
            }

            await webView.EnsureCoreWebView2Async();
        }
        finally
        {
            host.Children.Remove(webView);
        }
    }

    public void CloseWebView(WebView2 webView, Panel container)
    {
        // The macOS head leaks the WKWebView with no native destroy, and WebKit relaunches a renderer for the
        // still-alive view if the process is merely killed. Capture the native handle, then call WKWebView's
        // _close teardown SPI after the control leaves the tree: it terminates the renderer and marks the view
        // closed so it will not relaunch, reclaiming the per-renderer process.
        IntPtr nativeWebViewHandle = IntPtr.Zero;
        if (OperatingSystem.IsMacOS()
            && webView.CoreWebView2 is not null)
        {
            MacOSWebViewInterop.TryGetNativeWebViewHandle(webView.CoreWebView2, out nativeWebViewHandle, out _);
        }

        container.Children.Remove(webView);
        webView.Close();

        if (nativeWebViewHandle != IntPtr.Zero)
        {
            MacOSWebViewInterop.CloseNativeWebView(nativeWebViewHandle);
        }
    }

    public async Task<string> EvalAsync(CoreWebView2 coreWebView2, string expression)
    {
        // WKWebView's evaluateJavaScript faults on JS exceptions and syntax errors (WKError 4), on unsupported
        // return types such as Promises (WKError 5), and on an undefined result (surfaced by Uno as an
        // ArgumentNullException). WebView2 returns the JSON literal "null" silently in the equivalent cases.
        // Normalise the faults so common errors and undefined results read as None on Python callers across
        // platforms. Best-effort: exotic return values (Promise, Date, NaN, circular references) may still
        // serialise differently per platform. Agents are expected to adapt to the result they get.
        try
        {
            var result = await coreWebView2.ExecuteScriptAsync(expression);
            return result ?? "null";
        }
        catch (ArgumentNullException)
        {
            return "null";
        }
        catch (Exception scriptEx) when (scriptEx.Message.Contains("WKErrorDomain", StringComparison.Ordinal))
        {
            return "null";
        }
    }

    public async Task ReloadAsync(CoreWebView2 coreWebView2, bool clearCache)
    {
        // CoreWebView2.Profile (cache clear) and Reload() are not implemented on the Skia head, so reload
        // through the page. clearCache is best-effort here -- location.reload() does not purge the HTTP cache
        // (that would need WKWebsiteDataStore interop).
        await coreWebView2.ExecuteScriptAsync("location.reload()");
    }

    public async Task<ScreenshotData> CaptureScreenshotAsync(WebView2 webView, ScreenshotRequest request)
    {
        // Page.captureScreenshot (CDP) is not implemented, so snapshot the native WKWebView. The bridge resolves
        // the clip rect (viewport or selector) and a Scale that fits MaxEdge. Map that to the native snapshot:
        // clip to the rect, render at Width * Scale points.
        if (!MacOSWebViewInterop.TryGetNativeWebViewHandle(webView.CoreWebView2, out var nativeHandle, out var detail))
        {
            throw new InvalidOperationException(
                $"Could not resolve the native WKWebView for a screenshot. Walked: {detail}");
        }

        var clip = request.Clip;
        var snapshotRequest = new MacSnapshotRequest(
            clip?.X ?? 0,
            clip?.Y ?? 0,
            clip?.Width ?? 0,
            clip?.Height ?? 0,
            clip is not null ? clip.Width * clip.Scale : 0,
            request.Format,
            request.Quality);

        var snapshot = await MacOSWebViewInterop.TakeSnapshotAsync(nativeHandle, snapshotRequest);
        if (snapshot is null)
        {
            throw new InvalidOperationException(
                "The native WKWebView snapshot did not complete. The document tab must be the " +
                "active, visible tab for a screenshot.");
        }

        return new ScreenshotData(request.Format, snapshot.Width, snapshot.Height, snapshot.Bytes);
    }

    public void PostMessageToWeb(CoreWebView2 coreWebView2, string json)
    {
        // PostWebMessageAsString does not deliver on the Uno Skia WebView2 (the C#->JS half of web messaging is
        // unimplemented). Push the message by invoking a JS dispatch function via ExecuteScriptAsync, which the
        // client transport registers. The JS->C# direction (chrome.webview.postMessage -> WebMessageReceived)
        // works and is unchanged. Serializing the JSON yields a safely-escaped JS string literal.
        var encodedJson = JsonSerializer.Serialize(json);
        var script = $"window.__hostReceiveMessage && window.__hostReceiveMessage({encodedJson});";

        // ExecuteScriptAsync is the C#->JS push on Skia. Observe the operation instead of discarding it, so a
        // delivery fault (the script never ran) is surfaced rather than lost silently.
        var executeScriptOperation = coreWebView2.ExecuteScriptAsync(script);
        _ = ObserveExecuteScriptAsync();

        async Task ObserveExecuteScriptAsync()
        {
            try
            {
                await executeScriptOperation;
            }
            catch (Exception observeException)
            {
                _logger.LogError(observeException, "Failed to deliver host->editor message via ExecuteScriptAsync");
            }
        }
    }

    public async Task InstallDocumentStartScriptAsync(CoreWebView2 coreWebView2, string script)
    {
        // The Skia WebView2 does not implement AddScriptToExecuteOnDocumentCreatedAsync, so document-start
        // injection is native-only. macOS uses a WKUserScript; the desktop Windows head has no equivalent and
        // relies on the ReinjectDocumentStartScriptAsync (ExecuteScriptAsync) re-delivery after each navigation.
        if (OperatingSystem.IsMacOS()
            && MacOSWebViewInterop.TryGetNativeWebViewHandle(coreWebView2, out var nativeHandle, out _))
        {
            MacOSWebViewInterop.AddUserScriptAtDocumentStart(nativeHandle, script);
        }

        await Task.CompletedTask;
    }

    public async Task ReinjectDocumentStartScriptAsync(CoreWebView2 coreWebView2, string script)
    {
        await coreWebView2.ExecuteScriptAsync(script);
    }

    // The macOS WKWebView UA prefix (the OS and AppleWebKit build tokens) is frozen by Apple for fingerprinting
    // resistance, so it is stable to hardcode. The Version and Safari tokens are appended to match Safari's UA:
    // Gmail and similar sniffers reject the bare WKWebView UA (which omits both) as an unsupported browser. The
    // Version value is the installed Safari's real version, read at runtime so it never goes stale.
    private const string MacOSUserAgentPrefix =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko)";

    // Used only when the installed Safari version cannot be read. Kept comfortably above Gmail's minimum so the
    // UA still passes; the real version is preferred whenever available.
    private const string FallbackSafariVersion = "18.0";

    private string? _safariVersion;

    public void SetApplicationUserAgent(CoreWebView2 coreWebView2, string applicationToken)
    {
        if (!OperatingSystem.IsMacOS())
        {
            // The Linux/X11 Skia head's WebKitGTK UA is recognised as-is; only the macOS WKWebView UA needs the
            // Safari tokens, so leave the other Skia heads on their default.
            return;
        }

        if (!MacOSWebViewInterop.TryGetNativeWebViewHandle(coreWebView2, out var nativeHandle, out var detail))
        {
            _logger.LogWarning("Could not set the WebView User-Agent: {Detail}", detail);
            return;
        }

        _safariVersion ??= ResolveSafariVersion();

        var userAgent = $"{MacOSUserAgentPrefix} Version/{_safariVersion} Safari/605.1.15 {applicationToken}";
        MacOSWebViewInterop.SetCustomUserAgent(nativeHandle, userAgent);
    }

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

    public void LoadHtmlString(CoreWebView2 coreWebView2, string html, string baseUrl)
    {
        // Calls -[WKWebView loadHTMLString:baseURL:] so the loaded document reports the given base URL as its
        // origin. The macOS replacement for SetVirtualHostNameToFolderMapping, which is a silent no-op here.
        if (!MacOSWebViewInterop.TryGetNativeWebViewHandle(coreWebView2, out var nativeHandle, out var detail))
        {
            throw new InvalidOperationException(
                $"Could not reach the native WKWebView handle to load HTML: {detail}");
        }

        MacOSWebViewInterop.LoadHtmlString(nativeHandle, html, baseUrl);
    }

    private Panel EnsureInitHost()
    {
        if (_initHost is not null)
        {
            return _initHost;
        }

        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        if (userInterfaceService.MainWindow is not Window mainWindow ||
            mainWindow.Content is not Grid rootGrid)
        {
            throw new InvalidOperationException(
                "Cannot initialize WebView2: the application root grid is not available yet");
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
