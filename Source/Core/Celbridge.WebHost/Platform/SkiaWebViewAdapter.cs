using System.Text.Json;
using Celbridge.Logging;
using Celbridge.UserInterface;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost.Platform;

/// <summary>
/// IWebViewAdapter for the Uno Skia heads. Falls back to ExecuteScriptAsync where the managed CoreWebView2
/// surface is unimplemented and, on macOS, to the native WKWebView interop. The macOS fallbacks are
/// runtime-gated, so the same implementation also serves the desktop Windows head.
/// </summary>
public sealed class SkiaWebViewAdapter : IWebViewAdapter
{
    private readonly ILogger<SkiaWebViewAdapter> _logger;

    // Hidden, window-rooted host used to initialize WebView2 controls, where EnsureCoreWebView2Async never
    // completes for a control that has not been parented to a window.
    private Panel? _initHost;

    private bool _checkedBackgroundActivity;
    private bool _checkedInactiveSelection;

    // The find methods receive only a CoreWebView2, so sessions are keyed by it to recover per-find state.
    private readonly Dictionary<CoreWebView2, FindSession> _findSessions = new();

    private sealed record FindSession(string Term, bool CaseSensitive, Action<FindMatchState>? OnMatchStateChanged);

    public SkiaWebViewAdapter(ILogger<SkiaWebViewAdapter> logger)
    {
        _logger = logger;
    }

    // Windows-under-Skia hosts a real WebView2 that implements virtual-host mapping. macOS WKWebView and the
    // Linux Skia head do not, and use loadHTMLString instead.
    public bool SupportsVirtualHostMapping => OperatingSystem.IsWindows();

    // Windows-under-Skia hosts Chromium's WebView2 with its own find bar. The macOS WKWebView and Linux
    // WebKitGTK backends have none, so the host find bar drives find through this adapter there.
    public bool ProvidesBuiltInFind => OperatingSystem.IsWindows();

    // CoreWebView2.Profile is unimplemented on every Skia head. macOS clears through the native
    // WKWebsiteDataStore instead; the Windows and Linux Skia heads have no such path.
    public bool SupportsLiveBrowsingDataClear => OperatingSystem.IsMacOS();

    // The default WKWebsiteDataStore is process-wide, so the clear reaches it with no web view.
    public bool BrowsingDataClearRequiresInstance => false;

    public async Task EnsureCoreWebView2Async(WebView2 webView)
    {
        // EnsureCoreWebView2Async never completes for a control that is not parented to a window. Parent the
        // control in the hidden, window-rooted host for the duration of initialization, then detach it so the
        // consumer can place it in its own container with the CoreWebView2 already live.
        var host = await EnsureInitHostAsync();
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

            // Pin the native WKWebView for the process lifetime and keep it schedulable while hidden. Uno's
            // native element disposes the view on every Unloaded and later touches the stale handle, which is
            // a use-after-free. The handle may not be resolvable yet at this point, so the other adapter entry
            // points that resolve it also pin (RetainNativeWebView is idempotent).
            if (OperatingSystem.IsMacOS() && webView.CoreWebView2 is not null)
            {
                if (MacOSWebViewInterop.TryGetNativeWebViewHandle(webView.CoreWebView2, out var nativeWebViewHandle, out var detail))
                {
                    MacOSWebViewInterop.RetainNativeWebView(nativeWebViewHandle);
                    CheckBackgroundPageActivityOnce(nativeWebViewHandle);
                    KeepSelectionWhileUnfocused(nativeWebViewHandle);
                    ApplyInitialViewportSize(nativeWebViewHandle);
                }
                else
                {
                    _logger.LogDebug("Native WKWebView handle not resolvable after init ({Detail}); pinning deferred to first resolution", detail);
                }

                // UNO-BUG: the script message handler is registered on every Loaded and never removed.
                // Uno registers its script message handler on every Loaded and never removes it, so the
                // second load of a control aborts the process inside WebKit. This control sees a second
                // load as soon as it leaves the init host for its real container, so drop the handler on
                // every Unloaded and let Uno's next Loaded register it again.
                webView.Unloaded -= WebView_Unloaded;
                webView.Unloaded += WebView_Unloaded;
            }
        }
        finally
        {
            host.Children.Remove(webView);
        }
    }

    private void WebView_Unloaded(object sender, RoutedEventArgs e)
    {
        var webView = sender as WebView2;
        if (webView?.CoreWebView2 is null)
        {
            return;
        }

        if (MacOSWebViewInterop.TryGetNativeWebViewHandle(webView.CoreWebView2, out var nativeWebViewHandle, out var detail))
        {
            MacOSWebViewInterop.RemoveUnoScriptMessageHandler(nativeWebViewHandle);
        }
        else
        {
            _logger.LogWarning("Could not remove the WebView script message handler: {Detail}", detail);
        }
    }

    // Used until the window can supply a size, and as the floor for a window too small to lay a page out in.
    // A page that loads at this size is corrected by the arrange that follows when its surface is shown.
    private const double MinimumViewportWidth = 1024;
    private const double MinimumViewportHeight = 768;

    // UNO-BUG: the native frame is arranged only while the control is in the visual tree.
    // Gives the native view a usable frame before anything loads into it. Uno arranges the frame only while
    // the control is in the visual tree, so a surface that loads while it is not (a document restored into a
    // background tab, a utility running from project load) reports a zero-sized window to its page: layout
    // collapses, and a page that derives geometry from the viewport at startup divides by zero and stays
    // broken even after the real arrange arrives.
    private void ApplyInitialViewportSize(IntPtr nativeWebViewHandle)
    {
        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();

        double width = MinimumViewportWidth;
        double height = MinimumViewportHeight;

        if (userInterfaceService.MainWindow is Window mainWindow
            && mainWindow.Content is FrameworkElement windowContent)
        {
            width = Math.Max(windowContent.ActualWidth, MinimumViewportWidth);
            height = Math.Max(windowContent.ActualHeight, MinimumViewportHeight);
        }

        MacOSWebViewInterop.SetViewportSize(nativeWebViewHandle, width, height);
    }

    // WebKit suspends a hidden page's process, which stalls host-to-editor RPC for a background document
    // tab until the tab is shown again. RetainNativeWebView turns that suppression off through private SPI,
    // so this reports once per session whether the SPI still exists: losing it silently brings the stall back.
    private void CheckBackgroundPageActivityOnce(IntPtr nativeWebViewHandle)
    {
        if (_checkedBackgroundActivity)
        {
            return;
        }

        _checkedBackgroundActivity = true;

        var applied = MacOSWebViewInterop.EnableBackgroundPageActivity(nativeWebViewHandle);
        if (applied.Count == MacOSWebViewInterop.BackgroundPageActivityPreferenceCount)
        {
            _logger.LogDebug("Background page activity preferences applied: {Applied}", string.Join(", ", applied));
            return;
        }

        _logger.LogWarning(
            "WebKit no longer exposes every background page activity preference, so background documents may stop servicing host RPC. Applied: {Applied}",
            applied.Count == 0 ? "none" : string.Join(", ", applied));
    }

    // Managed focus moves resign the web view's first responder status, and WebKit discards the page's
    // selection when that happens, so a selection the user just made in a hosted page disappears. Reported
    // once per session because losing the SPI brings the disappearing selection back.
    private void KeepSelectionWhileUnfocused(IntPtr nativeWebViewHandle)
    {
        var maintained = MacOSWebViewInterop.MaintainInactiveSelection(nativeWebViewHandle);
        if (maintained)
        {
            if (!_checkedInactiveSelection)
            {
                _checkedInactiveSelection = true;
                _logger.LogDebug("Hosted pages keep their selection while unfocused");
            }

            return;
        }

        if (!_checkedInactiveSelection)
        {
            _checkedInactiveSelection = true;
            _logger.LogWarning(
                "WebKit no longer exposes the inactive selection setting, so a selection in a hosted page is lost when focus moves");
        }
    }

    public void CloseWebView(WebView2 webView, Panel? container)
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

        if (webView.CoreWebView2 is not null)
        {
            _findSessions.Remove(webView.CoreWebView2);
        }

        container?.Children.Remove(webView);
        webView.Close();

        if (nativeWebViewHandle != IntPtr.Zero)
        {
            MacOSWebViewInterop.CloseNativeWebView(nativeWebViewHandle);
        }
    }

    public void FocusWebView(WebView2 webView)
    {
        // On macOS programmatic managed focus flips the WebView's input routing to the managed pipeline,
        // where keys never reach the web content. Reproduce the responder state a click inside the view
        // establishes instead: make the native WKWebView the window's first responder. The reconciler yields
        // managed focus before this runs, because Uno resigns the native first responder whenever it
        // applies managed focus.
        if (OperatingSystem.IsMacOS()
            && webView.CoreWebView2 is not null)
        {
            if (MacOSWebViewInterop.TryGetNativeWebViewHandle(webView.CoreWebView2, out var nativeHandle, out var detail))
            {
                MacOSWebViewInterop.RetainNativeWebView(nativeHandle);
                MacOSWebViewInterop.MakeWebViewFirstResponder(nativeHandle);
            }
            else
            {
                _logger.LogWarning("Could not focus the WebView natively: {Detail}", detail);
            }

            return;
        }

        webView.Focus(FocusState.Programmatic);
    }

    public async Task<string> EvalAsync(CoreWebView2 coreWebView2, string expression)
    {
        // WKWebView's evaluateJavaScript faults on JS exceptions and syntax errors (WKError 4), on unsupported
        // return types such as Promises (WKError 5), and on an undefined result (surfaced by Uno as an
        // ArgumentNullException). WebView2 returns the JSON literal "null" silently in the equivalent cases.
        // Normalise the faults so common errors and undefined results read as None on Python callers across
        // platforms. Best-effort: exotic return values (Promise, Date, NaN, circular references) may still
        // serialise differently per platform.
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

    public async Task StopAsync(CoreWebView2 coreWebView2)
    {
        // CoreWebView2.Stop() is not implemented on the Skia heads. macOS stops natively, which also cancels a
        // load that has not yet produced a document.
        if (OperatingSystem.IsMacOS())
        {
            if (MacOSWebViewInterop.TryGetNativeWebViewHandle(coreWebView2, out var nativeHandle, out var detail))
            {
                MacOSWebViewInterop.StopLoading(nativeHandle);
                return;
            }

            _logger.LogWarning("Could not stop the page natively: {Detail}", detail);
        }

        // No native path here, so stop through the page. This only reaches a load that already has a JS context.
        await coreWebView2.ExecuteScriptAsync("window.stop()");
    }

    public async Task ClearBrowsingDataAsync(CoreWebView2? coreWebView2)
    {
        if (!OperatingSystem.IsMacOS())
        {
            await Task.CompletedTask;
            return;
        }

        var cleared = await MacOSWebViewInterop.ClearBrowsingDataAsync();
        if (!cleared)
        {
            throw new InvalidOperationException("The native WKWebsiteDataStore clear did not complete");
        }
    }

    public async Task<ScreenshotData> CaptureScreenshotAsync(WebView2 webView, ScreenshotRequest request)
    {
        // Page.captureScreenshot (CDP) is not implemented, so snapshot the native WKWebView. The bridge resolves
        // the clip rect (viewport or selector) and a Scale that fits MaxEdge. Map that to the native snapshot:
        // clip to the rect, render at Width * Scale device pixels (the native path divides out the backing scale).
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
        // UNO-BUG: PostWebMessageAsString is unimplemented on the Skia WebView2.
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
        // injection is native-only. macOS uses a WKUserScript. The desktop Windows head has no equivalent and
        // relies on the ReinjectDocumentStartScriptAsync (ExecuteScriptAsync) re-delivery after each navigation.
        if (OperatingSystem.IsMacOS()
            && MacOSWebViewInterop.TryGetNativeWebViewHandle(coreWebView2, out var nativeHandle, out _))
        {
            MacOSWebViewInterop.RetainNativeWebView(nativeHandle);
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
    // UA still passes. The real version is preferred whenever available.
    private const string FallbackSafariVersion = "18.0";

    private string? _safariVersion;

    public void SetApplicationUserAgent(CoreWebView2 coreWebView2, string applicationToken)
    {
        if (!OperatingSystem.IsMacOS())
        {
            // The Linux/X11 Skia head's WebKitGTK UA is recognised as-is. Only the macOS WKWebView UA needs the
            // Safari tokens, so leave the other Skia heads on their default.
            return;
        }

        if (!MacOSWebViewInterop.TryGetNativeWebViewHandle(coreWebView2, out var nativeHandle, out var detail))
        {
            _logger.LogWarning("Could not set the WebView User-Agent: {Detail}", detail);
            return;
        }

        MacOSWebViewInterop.RetainNativeWebView(nativeHandle);

        _safariVersion ??= ResolveSafariVersion();

        var userAgent = $"{MacOSUserAgentPrefix} Version/{_safariVersion} Safari/605.1.15 {applicationToken}";
        MacOSWebViewInterop.SetCustomUserAgent(nativeHandle, userAgent);
    }

    public void SetZoomControlEnabled(CoreWebView2 coreWebView2, bool enabled)
    {
        // The Skia heads' managed WebView2 surface does not implement zoom control, and user zoom is not
        // wired there, so there is nothing to toggle.
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

        MacOSWebViewInterop.RetainNativeWebView(nativeHandle);
        MacOSWebViewInterop.LoadHtmlString(nativeHandle, html, baseUrl);
    }

    public async Task StartFindAsync(CoreWebView2 coreWebView2, string term, FindOptions options)
    {
        await Task.CompletedTask;

        if (!OperatingSystem.IsMacOS())
        {
            // Whole-page find is wired for the macOS WKWebView only. The Linux WebKitGTK and Windows-under-Skia
            // heads have no native find plumbing here.
            _logger.LogDebug("Whole-page find is not implemented on this Skia head");
            return;
        }

        if (string.IsNullOrEmpty(term))
        {
            StopFind(coreWebView2);
            return;
        }

        if (!MacOSWebViewInterop.TryGetNativeWebViewHandle(coreWebView2, out var nativeHandle, out var detail))
        {
            _logger.LogWarning("Could not start find: {Detail}", detail);
            return;
        }

        MacOSWebViewInterop.RetainNativeWebView(nativeHandle);

        var session = new FindSession(term, options.CaseSensitive, options.OnMatchStateChanged);
        _findSessions[coreWebView2] = session;

        IssueFind(nativeHandle, session, backwards: false);
    }

    public void FindNext(CoreWebView2 coreWebView2)
    {
        StepFind(coreWebView2, backwards: false);
    }

    public void FindPrevious(CoreWebView2 coreWebView2)
    {
        StepFind(coreWebView2, backwards: true);
    }

    public void StopFind(CoreWebView2 coreWebView2)
    {
        _findSessions.Remove(coreWebView2);

        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        // findString leaves the last match selected. Clear it so no highlight lingers after the bar closes.
        var clearOperation = coreWebView2.ExecuteScriptAsync("window.getSelection().removeAllRanges()");
        _ = ObserveClearAsync();

        async Task ObserveClearAsync()
        {
            try
            {
                await clearOperation;
            }
            catch (Exception clearException)
            {
                _logger.LogError(clearException, "Failed to clear the find selection");
            }
        }
    }

    private void StepFind(CoreWebView2 coreWebView2, bool backwards)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        if (!_findSessions.TryGetValue(coreWebView2, out var session))
        {
            return;
        }

        if (!MacOSWebViewInterop.TryGetNativeWebViewHandle(coreWebView2, out var nativeHandle, out _))
        {
            return;
        }

        IssueFind(nativeHandle, session, backwards);
    }

    private static void IssueFind(IntPtr nativeHandle, FindSession session, bool backwards)
    {
        // Find always wraps, matching browser behaviour.
        MacOSWebViewInterop.FindString(
            nativeHandle,
            session.Term,
            session.CaseSensitive,
            backwards,
            wraps: true,
            matchFound => session.OnMatchStateChanged?.Invoke(new FindMatchState(matchFound)));
    }

    private async Task<Panel> EnsureInitHostAsync()
    {
        if (_initHost is not null)
        {
            return _initHost;
        }

        // The factory pre-warms its pool from application startup, before the window content exists, so
        // wait for the root grid rather than failing the early pool instances.
        var userInterfaceService = ServiceLocator.AcquireService<IUserInterfaceService>();
        var pollInterval = TimeSpan.FromMilliseconds(100);
        var rootGridWait = TimeSpan.Zero;

        Grid? rootGrid = null;
        while (rootGrid is null)
        {
            if (userInterfaceService.MainWindow is Window mainWindow &&
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
