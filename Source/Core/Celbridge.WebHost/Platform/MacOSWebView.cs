using System.Text.Json;
using Celbridge.Logging;
using Celbridge.WebHost.Services;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost.Platform;

/// <summary>
/// A web view on the macOS Skia head. It reaches the native WKWebView for what Uno leaves unimplemented, and wakes
/// its page while the page is hidden.
/// </summary>
internal sealed class MacOSWebView : SkiaWebView
{
    // How long a hosted page may go without being woken. A hidden page's event loop stops entirely after
    // roughly seven minutes, so a page that has gone quiet is running again well inside the timeouts that
    // wait on it.
    private const int KeepAliveIntervalSeconds = 30;

    // Spread added to every wake, so pages created together do not settle into waking in the same instant.
    private const int KeepAliveJitterSeconds = 5;

    // How often a page that keeps missing its wake repeats the report.
    private const int FailuresPerReport = 10;

    // How long a wake may go unanswered before it counts as missed. A page that is running answers in
    // milliseconds, so this only has to outlast a page busy with its own work.
    private const int WakeTimeoutSeconds = 10;

    // The macOS WKWebView UA prefix (the OS and AppleWebKit build tokens) is frozen by Apple for fingerprinting
    // resistance, so it is stable to hardcode. The Version and Safari tokens are appended to match Safari's UA:
    // Gmail and similar sniffers reject the bare WKWebView UA (which omits both) as an unsupported browser. The
    // Version value is the installed Safari's real version, read at runtime so it never goes stale.
    private const string UserAgentPrefix =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko)";

    private readonly CoreWebView2 _coreWebView2;
    private readonly SkiaWebViewPlatform _platform;
    private readonly ILogger _logger;

    // Ends the wake loop when the view closes.
    private readonly CancellationTokenSource _keepAliveCancellation = new();

    private sealed record FindSession(string Term, bool CaseSensitive, Action<FindMatchState>? OnMatchStateChanged);

    private FindSession? _findSession;

    // The base URL of the last page loaded from an HTML string. The page is still showing while the view's
    // address is this one.
    private string? _htmlStringBaseUrl;

    public MacOSWebView(WebView2 control, SkiaWebViewPlatform platform, ILogger logger)
        : base(control, platform, logger)
    {
        _coreWebView2 = CoreWebView2!;
        _platform = platform;
        _logger = logger;

        MacOSHostedViewOpacityRepair.RepairOnAttach(control);

        _ = KeepPageAwakeAsync(_keepAliveCancellation.Token);
    }

    private bool TryGetNativeHandle(out IntPtr nativeHandle, out string detail)
    {
        return MacOSWebViewInterop.TryGetNativeWebViewHandle(_coreWebView2, out nativeHandle, out detail);
    }

    protected override void ApplyOptions(WebViewOptions options)
    {
        base.ApplyOptions(options);

        ApplyDevToolsState(options.IsDevToolsEnabled);

        if (!string.IsNullOrEmpty(options.UserAgentToken))
        {
            SetApplicationUserAgent(options.UserAgentToken);
        }
    }

    // Safari's Develop menu lists the page under its accessible name.
    protected override void OnAccessibleNameChanged()
    {
        if (!Options.IsDevToolsEnabled)
        {
            return;
        }

        ApplyDevToolsState(enabled: true);
    }

    // The WebView2 setting never reaches WebKit, so the native view is opted into remote inspection instead. That
    // lists the page in Safari's Develop menu, which otherwise identifies every hosted editor by its index.html URL.
    private void ApplyDevToolsState(bool enabled)
    {
        if (!TryGetNativeHandle(out var nativeHandle, out var detail))
        {
            _logger.LogWarning("Could not set the WebView developer tools state: {Detail}", detail);
            return;
        }

        _platform.PinNativeWebView(nativeHandle);

        var inspectable = MacOSWebViewInterop.SetInspectable(nativeHandle, enabled);

        var named = !enabled
            || MacOSWebViewInterop.SetRemoteInspectionName(nativeHandle, AccessibleName);

        _platform.ReportRemoteInspectionOnce(enabled, inspectable && named);
    }

    // The default WKWebView UA omits the Safari token some sites sniff for, and they flag it as unsupported. It is
    // replaced with a Safari-compatible UA that carries the application token.
    private void SetApplicationUserAgent(string applicationToken)
    {
        if (!TryGetNativeHandle(out var nativeHandle, out var detail))
        {
            _logger.LogWarning("Could not set the WebView User-Agent: {Detail}", detail);
            return;
        }

        _platform.PinNativeWebView(nativeHandle);

        var userAgent = $"{UserAgentPrefix} Version/{_platform.SafariVersion} Safari/605.1.15 {applicationToken}";
        MacOSWebViewInterop.SetCustomUserAgent(nativeHandle, userAgent);
    }

    // Calls -[WKWebView loadHTMLString:baseURL:], so the document has the base URL as its origin. Uno serves a
    // mapped virtual host from a file URL here, so a page that needs the host as its origin is loaded this way.
    public override void LoadHtmlString(string html, string baseUrl)
    {
        if (!TryGetNativeHandle(out var nativeHandle, out var detail))
        {
            throw new InvalidOperationException(
                $"Could not reach the native WKWebView handle to load HTML: {detail}");
        }

        _platform.PinNativeWebView(nativeHandle);
        MacOSWebViewInterop.LoadHtmlString(nativeHandle, html, baseUrl);

        _htmlStringBaseUrl = baseUrl;
    }

    // WebKit reloads a page loaded from an HTML string by requesting its base URL. That loads something other than
    // the string, or fails for an address that only names an origin, such as the spreadsheet's. The page is then
    // left blank, and its editor never reports its content ready. Only loading the string again restores it.
    protected override async Task ReloadPageAsync(bool clearCache)
    {
        if (IsShowingHtmlString())
        {
            throw new NotSupportedException(
                "This page was loaded from an HTML string, which WebKit cannot reload. " +
                "Close and reopen the document to load it again.");
        }

        await base.ReloadPageAsync(clearCache);
    }

    private bool IsShowingHtmlString()
    {
        return _htmlStringBaseUrl is not null
            && Uri.TryCreate(_htmlStringBaseUrl, UriKind.Absolute, out var baseUri)
            && Uri.TryCreate(Source, UriKind.Absolute, out var sourceUri)
            && baseUri == sourceUri;
    }

    public override async Task StartFindAsync(string term, FindOptions options)
    {
        await Task.CompletedTask;

        if (string.IsNullOrEmpty(term))
        {
            StopFind();
            return;
        }

        if (!TryGetNativeHandle(out var nativeHandle, out var detail))
        {
            _logger.LogWarning("Could not start find: {Detail}", detail);
            return;
        }

        _platform.PinNativeWebView(nativeHandle);

        var session = new FindSession(term, options.CaseSensitive, options.OnMatchStateChanged);
        _findSession = session;

        IssueFind(nativeHandle, session, backwards: false);
    }

    public override void FindNext()
    {
        StepFind(backwards: false);
    }

    public override void FindPrevious()
    {
        StepFind(backwards: true);
    }

    public override void StopFind()
    {
        _findSession = null;

        // findString leaves the last match selected. Clear it so no highlight lingers after the bar closes.
        var clearOperation = _coreWebView2.ExecuteScriptAsync("window.getSelection().removeAllRanges()");
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

    private void StepFind(bool backwards)
    {
        var session = _findSession;
        if (session is null)
        {
            return;
        }

        if (!TryGetNativeHandle(out var nativeHandle, out _))
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

    // A WKUserScript runs the script at document start on every later navigation.
    protected internal override async Task InstallDocumentStartScriptAsync(string script)
    {
        await Task.CompletedTask;

        if (!TryGetNativeHandle(out var nativeHandle, out _))
        {
            return;
        }

        _platform.PinNativeWebView(nativeHandle);
        MacOSWebViewInterop.AddUserScriptAtDocumentStart(nativeHandle, script);
    }

    // Uno pushes the frame on its own arrange pass, a beat after the control has its size, and the page can
    // measure inside that gap.
    protected override bool SetNativeViewportSize(double width, double height)
    {
        if (!TryGetNativeHandle(out var nativeHandle, out _))
        {
            return false;
        }

        MacOSWebViewInterop.SetViewportSize(nativeHandle, width, height);

        return true;
    }

    // Programmatic managed focus flips the WebView's input routing to the managed pipeline, where keys never
    // reach the web content. Making the native WKWebView the window's first responder reproduces the state a
    // click inside the view establishes. The reconciler yields managed focus before this runs, because Uno
    // resigns the native first responder whenever it applies managed focus.
    internal override void FocusPage()
    {
        if (!TryGetNativeHandle(out var nativeHandle, out var detail))
        {
            _logger.LogWarning("Could not focus the WebView natively: {Detail}", detail);
            return;
        }

        _platform.PinNativeWebView(nativeHandle);
        MacOSWebViewInterop.MakeWebViewFirstResponder(nativeHandle);
    }

    // On the macOS Skia head, whether the WKWebView sits in a window decides whether a load it starts runs on a
    // surface the platform can see.
    internal override string DescribeNativeSurface()
    {
        if (!TryGetNativeHandle(out var nativeHandle, out _))
        {
            return "native=unresolved";
        }

        var frame = MacOSWebViewInterop.GetFrame(nativeHandle);
        var window = MacOSWebViewInterop.HasWindow(nativeHandle) ? "yes" : "no";
        var processId = MacOSWebViewInterop.GetWebContentProcessId(nativeHandle);

        var describedFrame = frame is null
            ? "frame=none"
            : $"frame={frame.Width:F0}x{frame.Height:F0}@{frame.X:F0},{frame.Y:F0}";

        return $"window={window} {describedFrame} pid={processId}";
    }

    // The page encodes its value itself. A value JSON cannot represent then reads as null.
    protected override async Task<string> EvaluateAsync(string expression)
    {
        var encodedResult = await _coreWebView2.ExecuteScriptAsync(BuildPageEncodedScript(expression));

        return DecodePageEncodedResult(encodedResult);
    }

    /// <summary>
    /// Wraps an expression so that the page encodes its value as JSON. The script returns that JSON as the
    /// only string in an array. If the value cannot be encoded, the script throws, and the result reads as
    /// null, as it does on WebView2.
    /// </summary>
    // UNO-BUG: Uno encodes the result with NSJSONSerialization. A value that refers to itself, such as window,
    // makes it recurse until the main thread's stack overflows, and the application hangs. Uno also escapes
    // the quotes in a returned string but not its backslashes. NSJSONSerialization escapes an array
    // correctly, so the JSON comes back inside one.
    internal static string BuildPageEncodedScript(string expression)
    {
        // A trailing semicolon is not allowed inside the parentheses. The line breaks stop a trailing line
        // comment from hiding the closing parentheses.
        var trimmedExpression = expression.TrimEnd().TrimEnd(';');

        return $"[JSON.stringify((\n{trimmedExpression}\n)) ?? null]";
    }

    /// <summary>
    /// Returns the page's JSON, unwrapped from the array it arrives in.
    /// </summary>
    internal static string DecodePageEncodedResult(string? result)
    {
        if (string.IsNullOrEmpty(result))
        {
            return "null";
        }

        using var document = JsonDocument.Parse(result);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array ||
            root.GetArrayLength() != 1 ||
            root[0].ValueKind != JsonValueKind.String)
        {
            return "null";
        }

        return root[0].GetString() ?? "null";
    }

    // Page.captureScreenshot (CDP) is not implemented, so the native WKWebView is snapshotted. The bridge resolves
    // the clip rect (viewport or selector) and a Scale that fits MaxEdge. The snapshot clips to the rect and
    // renders at Width * Scale device pixels, since the native path divides out the backing scale.
    protected override async Task<ScreenshotData> CaptureScreenshotCoreAsync(ScreenshotRequest request)
    {
        if (!TryGetNativeHandle(out var nativeHandle, out var detail))
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

    // WebKit takes the download, since no Skia head raises WebView2's DownloadStarting.
    protected override IWebViewDownloadHandler CreateDownloadHandler()
    {
        return _platform.RouteDownloads(_coreWebView2);
    }

    // Uno changes Source only once a page has finished loading, or for a fragment link, so WebKit's own commit
    // reports a new page as it arrives.
    protected override IDisposable ObserveNavigationCommits(NavigationCommitted onCommitted)
    {
        var sourceObserver = base.ObserveNavigationCommits(onCommitted);

        if (!TryGetNativeHandle(out var nativeHandle, out var detail))
        {
            _logger.LogWarning("A page's navigations are reported only once it has finished loading: its native view could not be resolved ({Detail})", detail);
            return sourceObserver;
        }

        var commitRegistration = MacOSWebViewInterop.ObserveNavigationCommits(
            nativeHandle,
            url => ReportNavigationCommit(onCommitted, url),
            out var commitDetail);
        if (commitRegistration is null)
        {
            _logger.LogWarning("A page's navigations are reported only once it has finished loading: {Detail}", commitDetail);
            return sourceObserver;
        }

        return new PageRegistration(sourceObserver, commitRegistration);
    }

    // Reports WebKit's address in the form Uno gives Source, so a commit and the finished load that
    // follows name the page alike. Runs inside WebKit's commit callback, so a failing handler is contained
    // here.
    private void ReportNavigationCommit(NavigationCommitted onCommitted, string url)
    {
        var committedUrl = url;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            committedUrl = uri.ToString();
        }

        try
        {
            onCommitted(committedUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to report a navigation commit");
        }
    }

    // Wakes the page until the view closes. WebKit stops a hidden page's event loop after a few minutes, so it
    // services no host RPC until the user activates it, and evaluating a trivial script restarts it. The delay
    // must resume on the UI thread, where the script evaluation has to run, so the awaits here are never
    // configured away from the dispatcher.
    private async Task KeepPageAwakeAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, KeepAliveJitterSeconds * 1000));
                await Task.Delay(TimeSpan.FromSeconds(KeepAliveIntervalSeconds) + jitter, cancellationToken);

                await WakePageAsync(cancellationToken);

                var clearedFailures = Health.RecordWakeSucceeded();
                if (clearedFailures > 0)
                {
                    _logger.LogInformation(
                        "A hosted page is responding again after {FailureCount} missed wake(s)",
                        clearedFailures);
                }

                ObserveWebContentProcess();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                // The view was torn down without closing, so there is nothing left to wake.
                return;
            }
            catch (Exception ex)
            {
                var consecutiveFailures = Health.RecordWakeFailed();

                // A page that misses every wake would otherwise report itself on each one.
                if (consecutiveFailures == 1
                    || consecutiveFailures % FailuresPerReport == 0)
                {
                    _logger.LogWarning(
                        ex,
                        "A hosted page has missed {FailureCount} consecutive wake(s)",
                        consecutiveFailures);
                }

                // A dead renderer faults the wake rather than answering it, so the process is read on
                // the failure path too.
                ObserveWebContentProcess();
            }
        }
    }

    // Faults are the signal here, so this deliberately bypasses EvalAsync, which reports a page that
    // faulted and a page that returned undefined identically.
    private async Task WakePageAsync(CancellationToken cancellationToken)
    {
        var wakeTask = _coreWebView2.ExecuteScriptAsync("0").AsTask(cancellationToken);
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(WakeTimeoutSeconds), cancellationToken);

        if (await Task.WhenAny(wakeTask, timeoutTask) == timeoutTask)
        {
            // WebKit runs the completion handler on the page's own run loop, so a page whose loop has
            // stopped never answers and never faults. Without this the loop would await it forever and
            // silently stop waking the page.
            ObserveAbandonedTask(wakeTask);
            throw new TimeoutException($"The page did not answer a wake within {WakeTimeoutSeconds}s");
        }

        await wakeTask;
    }

    private static void ObserveAbandonedTask(Task task)
    {
        _ = task.ContinueWith(
            static abandonedTask => { _ = abandonedTask.Exception; },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    // Reads which process is rendering the page and reports what changed since the last reading. The page is
    // named, because a report that names none leaves the reader guessing which of the open documents it is.
    private void ObserveWebContentProcess()
    {
        var change = Health.RecordProcessId(ReadWebContentProcessId());
        if (change == PageProcessChange.None)
        {
            return;
        }

        var pageUrl = DescribePageUrl();

        switch (change)
        {
            case PageProcessChange.Gone:
                _logger.LogWarning("The WebContent process behind {PageUrl} is no longer running", pageUrl);
                break;

            case PageProcessChange.Relaunched:
                _logger.LogInformation("WebKit relaunched the WebContent process behind {PageUrl}", pageUrl);
                break;

            case PageProcessChange.Replaced:
                _logger.LogInformation(
                    "WebKit swapped the WebContent process behind {PageUrl} without a navigation", pageUrl);
                break;
        }
    }

    // Negative when the native view cannot be reached, which a page with no running renderer reports as zero
    // and must not be confused with.
    private long ReadWebContentProcessId()
    {
        if (!TryGetNativeHandle(out var nativeHandle, out _))
        {
            return -1;
        }

        return MacOSWebViewInterop.GetWebContentProcessId(nativeHandle);
    }

    private string DescribePageUrl()
    {
        // A page whose renderer has gone reports no address of its own, so the one recorded when it
        // navigated is the fallback, and only a page that never navigated goes unnamed.
        var address = Health.Address;
        if (!string.IsNullOrEmpty(address))
        {
            return address;
        }

        try
        {
            var source = _coreWebView2.Source;

            return string.IsNullOrEmpty(source) ? "a hosted page" : source;
        }
        catch (Exception)
        {
            return "a hosted page";
        }
    }

    protected override void ReleaseResources()
    {
        _keepAliveCancellation.Cancel();
        _keepAliveCancellation.Dispose();

        _findSession = null;

        base.ReleaseResources();
    }

    // The macOS head leaks the WKWebView with no native destroy, and WebKit relaunches a renderer for the
    // still-alive view if the process is merely killed. WKWebView's _close teardown SPI runs after the control
    // leaves the tree. It ends the renderer and marks the view closed, so it is not relaunched.
    protected override void CloseControl(Panel? container)
    {
        TryGetNativeHandle(out var nativeHandle, out _);

        // The dispose hook keeps a detached page loading, so a closing page is stopped here. This needs no
        // native handle, so it still works when the teardown below cannot run.
        StopLoading();

        try
        {
            base.CloseControl(container);
        }
        finally
        {
            if (nativeHandle != IntPtr.Zero)
            {
                MacOSWebViewInterop.CloseNativeWebView(nativeHandle);
            }
        }
    }

    private void StopLoading()
    {
        try
        {
            _coreWebView2.Stop();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not stop a closing web view's page from loading");
        }
    }
}
