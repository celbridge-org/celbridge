using System.Text.Json;
using Celbridge.Logging;
using Celbridge.WebHost.Services;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost.Platform;

/// <summary>
/// A web view on the macOS Skia head. It reaches the native WKWebView for what Uno leaves unimplemented, and wakes
/// its page while the page is hidden.
/// </summary>
public sealed class MacOSWebView : SkiaWebView
{
    // How long a hosted page may go without being woken. A hidden page's event loop stops entirely after
    // roughly seven minutes. At this interval a stopped page is running again well inside the timeouts that
    // wait on it.
    private const int KeepAliveIntervalSeconds = 30;

    // Spread added to every wake, so pages created together do not settle into waking in the same instant.
    private const int KeepAliveJitterSeconds = 5;

    // How often a page that keeps missing its wake repeats the report.
    private const int FailuresPerReport = 10;

    // How long a wake may go unanswered before it counts as missed. A page that is running answers in
    // milliseconds, so this only has to outlast a page busy with its own work.
    private const int WakeTimeoutSeconds = 10;

    // The OS and AppleWebKit build tokens of the macOS WKWebView UA. Apple froze these tokens to resist
    // fingerprinting, so they are safe to hardcode.
    private const string UserAgentPrefix =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko)";

    // Every view whose native view has resolved, keyed by its native handle. WebKit and AppKit callbacks carry only
    // the native view, and this map finds the view from it. A pinned native view stays allocated for the life of the
    // process, so its address always belongs to the same view. Used only on the main thread.
    private static readonly Dictionary<IntPtr, MacOSWebView> ViewsByNativeHandle = new();

    private readonly CoreWebView2 _coreWebView2;
    private readonly SkiaWebViewPlatform _platform;
    private readonly ILogger _logger;

    // The native WKWebView, held from the moment it resolves. A held native view is pinned.
    private IntPtr _nativeHandle;

    // Receives the commits WebKit reports, once something observes them.
    private NavigationCommitted? _onNativeCommit;

    // Ends the wake loop when the view closes.
    private readonly CancellationTokenSource _keepAliveCancellation = new();

    private sealed record FindSession(string Term, bool CaseSensitive, Action<FindMatchState>? OnMatchStateChanged);

    private FindSession? _findSession;

    // The base URL of the last page loaded from an HTML string. That page is still showing while the view's
    // address matches this URL.
    private string? _htmlStringBaseUrl;

    // The platform resolves the native view and pins it while the control is still in its init host. The platform
    // passes zero when the native view did not resolve, and the view resolves the native view later.
    internal MacOSWebView(WebView2 control, IntPtr nativeHandle, SkiaWebViewPlatform platform, ILogger logger)
        : base(control, platform, logger)
    {
        _coreWebView2 = CoreWebView2!;
        _platform = platform;
        _logger = logger;

        if (nativeHandle != IntPtr.Zero)
        {
            HoldNativeHandle(nativeHandle);
        }

        MacOSHostedViewOpacityRepair.RepairOnAttach(control);

        _ = KeepPageAwakeAsync(_keepAliveCancellation.Token);
    }

    /// <summary>
    /// Returns the view whose native WKWebView has this handle, or null when no view holds it.
    /// </summary>
    internal static MacOSWebView? FromNativeHandle(IntPtr nativeHandle)
    {
        return ViewsByNativeHandle.GetValueOrDefault(nativeHandle);
    }

    /// <summary>
    /// Gets the native WKWebView behind the view. A native view that has not resolved yet is resolved and pinned
    /// here. Returns false with the reason in detail when it cannot be resolved. Call on the main thread.
    /// </summary>
    public bool TryGetNativeHandle(out IntPtr nativeHandle, out string detail)
    {
        if (_nativeHandle == IntPtr.Zero)
        {
            // A closed view stays out of the map, so the resolve stops here.
            if (IsDisposed)
            {
                nativeHandle = IntPtr.Zero;
                detail = "the web view has closed";
                return false;
            }

            if (!MacOSWebViewInterop.TryGetNativeWebViewHandle(_coreWebView2, out var resolvedHandle, out detail))
            {
                nativeHandle = IntPtr.Zero;
                return false;
            }

            _platform.PinNativeWebView(resolvedHandle);
            HoldNativeHandle(resolvedHandle);
        }

        nativeHandle = _nativeHandle;
        detail = string.Empty;
        return true;
    }

    private void HoldNativeHandle(IntPtr nativeHandle)
    {
        _nativeHandle = nativeHandle;
        ViewsByNativeHandle[nativeHandle] = this;
    }

    // Clicks, downloads and commits reach the view only after its native view resolves, so a failure to resolve is
    // reported. A later resolution to a different native view means Uno replaced the native view, and this view
    // still holds the old one.
    protected override void OnAttached()
    {
        if (_nativeHandle == IntPtr.Zero)
        {
            if (!TryGetNativeHandle(out _, out var detail))
            {
                _logger.LogWarning(
                    "The native view of the web view for {Resource} could not be resolved, so its clicks, downloads and navigation commits are not reported: {Detail}",
                    Resource,
                    detail);
            }

            return;
        }

        if (MacOSWebViewInterop.TryGetNativeWebViewHandle(_coreWebView2, out var currentHandle, out _) &&
            currentHandle != _nativeHandle)
        {
            _logger.LogWarning(
                "Uno replaced the native view of the web view for {Resource}, so its clicks, downloads and navigation commits are no longer reported",
                Resource);
        }
    }

    /// <summary>
    /// Called on the main thread when a click lands in the view.
    /// </summary>
    internal void OnClicked()
    {
        RaiseFocusGained();
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

    // The WebView2 developer tools setting never reaches WebKit, so the native view is opted into remote
    // inspection instead. Remote inspection lists the page in Safari's Develop menu under its accessible name.
    // Without the name, the menu shows every hosted editor as its index.html URL.
    private void ApplyDevToolsState(bool enabled)
    {
        if (!TryGetNativeHandle(out var nativeHandle, out var detail))
        {
            _logger.LogWarning("Could not set the WebView developer tools state: {Detail}", detail);
            return;
        }

        var inspectable = MacOSWebViewInterop.SetInspectable(nativeHandle, enabled);

        var named = !enabled
            || MacOSWebViewInterop.SetRemoteInspectionName(nativeHandle, AccessibleName);

        _platform.ReportRemoteInspectionOnce(enabled, inspectable && named);
    }

    // Replaces the default WKWebView UA with Safari's, plus the application token. The default UA omits the
    // Version and Safari tokens, and some sites, Gmail among them, reject it as an unsupported browser. The
    // Version value is the installed Safari's real version, read at runtime so it never goes stale.
    private void SetApplicationUserAgent(string applicationToken)
    {
        if (!TryGetNativeHandle(out var nativeHandle, out var detail))
        {
            _logger.LogWarning("Could not set the WebView User-Agent: {Detail}", detail);
            return;
        }

        var userAgent = $"{UserAgentPrefix} Version/{_platform.SafariVersion} Safari/605.1.15 {applicationToken}";
        MacOSWebViewInterop.SetCustomUserAgent(nativeHandle, userAgent);
    }

    // Calls -[WKWebView loadHTMLString:baseURL:], which gives the document the base URL as its origin. On this
    // head Uno serves a mapped virtual host from a file URL. A page that needs the virtual host as its origin is
    // therefore loaded from a string.
    protected override void LoadHtmlStringCore(string html, string baseUrl)
    {
        if (!TryGetNativeHandle(out var nativeHandle, out var detail))
        {
            throw new InvalidOperationException(
                $"Could not reach the native WKWebView handle to load HTML: {detail}");
        }

        MacOSWebViewInterop.LoadHtmlString(nativeHandle, html, baseUrl);

        _htmlStringBaseUrl = baseUrl;
    }

    // WebKit reloads a page loaded from an HTML string by requesting the base URL. That request loads some other
    // page, or fails when the base URL names only an origin, as the spreadsheet's does. Either way the page is left
    // blank, and its editor never reports its content ready. Only loading the string again restores the page.
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

    // A WKUserScript runs the script at document start on every later navigation. Without the native view, the
    // script runs after each navigation instead.
    protected override async Task<bool> InstallDocumentStartScriptAsync(string script)
    {
        await Task.CompletedTask;

        if (!TryGetNativeHandle(out var nativeHandle, out var detail))
        {
            _logger.LogWarning("Could not install a document-start script, so it runs after each navigation instead: {Detail}", detail);
            return false;
        }

        MacOSWebViewInterop.AddUserScriptAtDocumentStart(nativeHandle, script);

        return true;
    }

    // Uno pushes the native frame on its own arrange pass, a beat after the control has its size. The page can
    // measure inside that gap, so the frame is set here as soon as the size is known.
    protected override bool SetNativeViewportSize(double width, double height)
    {
        if (!TryGetNativeHandle(out var nativeHandle, out _))
        {
            return false;
        }

        MacOSWebViewInterop.SetViewportSize(nativeHandle, width, height);

        return true;
    }

    // Makes the native WKWebView the window's first responder, the state a click inside the view sets up.
    // Programmatic managed focus would flip the WebView's input routing to the managed pipeline, where keys never
    // reach the web content. The reconciler yields managed focus before this runs. Uno resigns the native first
    // responder whenever it applies managed focus.
    internal override void FocusPage()
    {
        if (!TryGetNativeHandle(out var nativeHandle, out var detail))
        {
            _logger.LogWarning("Could not focus the WebView natively: {Detail}", detail);
            return;
        }

        MacOSWebViewInterop.MakeWebViewFirstResponder(nativeHandle);
    }

    // The keyboard goes to the native view inside the control, so this asks macOS whether the native view is the
    // window's first responder. The page's own focus reports cannot be trusted here. Whenever the host moves
    // focus, the native view loses first responder and gets it straight back. The page reports that brief gap as
    // an ordinary blur.
    internal override bool HoldsKeyboard()
    {
        if (!TryGetNativeHandle(out var nativeHandle, out var detail))
        {
            _logger.LogWarning("Could not read the web view's native focus: {Detail}", detail);
            return false;
        }

        return MacOSWebViewInterop.IsWebViewFirstResponder(nativeHandle);
    }

    // A direct call to the native view's keyDown:, so local event monitors do not see the key again.
    internal override bool SendKeyDown(IntPtr nativeKeyEvent)
    {
        if (!TryGetNativeHandle(out var nativeHandle, out var detail))
        {
            _logger.LogWarning("Could not deliver a key to the web view: {Detail}", detail);
            return false;
        }

        MacOSWebViewInterop.SendKeyDownToWebView(nativeHandle, nativeKeyEvent);

        return true;
    }

    // WebKit sends a key the page left unhandled back through the application. The page has already seen that
    // key, so it is not sent again.
    internal override bool ForwardKeyDown(IntPtr nativeKeyEvent)
    {
        if (MacOSWebViewInterop.HasWebViewReceivedKeyDown(nativeKeyEvent))
        {
            return false;
        }

        return SendKeyDown(nativeKeyEvent);
    }

    // On the macOS Skia head, a load runs on a surface the platform can see only when the WKWebView sits in a
    // window.
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
    // makes NSJSONSerialization recurse until the main thread's stack overflows, and the application hangs. Uno
    // also escapes the quotes in a returned string but not its backslashes. NSJSONSerialization escapes an array
    // correctly, so the page's JSON comes back inside an array.
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

    // WebKit takes the download, since the Skia heads never raise WebView2's DownloadStarting. A view whose native
    // view is still unresolved gets an inert handler.
    protected override IWebViewDownloadHandler CreateDownloadHandler()
    {
        TryGetNativeHandle(out var nativeHandle, out _);

        return _platform.RouteDownloads(nativeHandle);
    }

    // Uno changes Source only when a page finishes loading or follows a fragment link. WebKit's own commit reports
    // a new page as it arrives. WebKit's commits stop reaching the view once the view closes and leaves the map.
    protected override IDisposable ObserveNavigationCommits(NavigationCommitted onCommitted)
    {
        var sourceObserver = base.ObserveNavigationCommits(onCommitted);

        if (!TryGetNativeHandle(out var nativeHandle, out var detail))
        {
            _logger.LogWarning("A page's navigations are reported only once it has finished loading: its native view could not be resolved ({Detail})", detail);
            return sourceObserver;
        }

        if (!MacOSWebViewInterop.ObserveNavigationCommits(nativeHandle, OnNativeNavigationCommitted, out var commitDetail))
        {
            _logger.LogWarning("A page's navigations are reported only once it has finished loading: {Detail}", commitDetail);
            return sourceObserver;
        }

        _onNativeCommit = onCommitted;

        return sourceObserver;
    }

    // WebKit reports every web view's commits here, with the native view that committed.
    private static void OnNativeNavigationCommitted(IntPtr nativeHandle, string url)
    {
        FromNativeHandle(nativeHandle)?.ReportNavigationCommit(url);
    }

    // Reports WebKit's address in the form Uno gives Source, so a commit and the finished load that follows
    // name the page alike. This runs inside WebKit's commit callback, so a failing handler is caught here.
    private void ReportNavigationCommit(string url)
    {
        var onCommitted = _onNativeCommit;
        if (onCommitted is null)
        {
            return;
        }

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

    // Wakes the page until the view closes. WebKit stops a hidden page's event loop after a few minutes. A
    // stopped page services no host RPC until the user activates it. Evaluating a trivial script restarts the
    // event loop. The script evaluation has to run on the UI thread, so the awaits here are never configured
    // away from the dispatcher.
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
                // The view was torn down before it closed, so the wake ends here.
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

                // A dead renderer faults the wake, so the process is read on the failure path too.
                ObserveWebContentProcess();
            }
        }
    }

    // The wake needs to see faults, so the script runs on the core directly. EvalAsync reports a page that
    // faulted and a page that returned undefined identically.
    private async Task WakePageAsync(CancellationToken cancellationToken)
    {
        var wakeTask = _coreWebView2.ExecuteScriptAsync("0").AsTask(cancellationToken);
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(WakeTimeoutSeconds), cancellationToken);

        if (await Task.WhenAny(wakeTask, timeoutTask) == timeoutTask)
        {
            // WebKit runs the completion handler on the page's own run loop. A page whose run loop has
            // stopped stays silent forever. The timeout ends the wait, so the wake loop records the miss and
            // carries on.
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

    // Reads which process is rendering the page and reports what changed since the last reading. Each report
    // names the page, so the reader can tell which open document it concerns.
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

    // Negative when the native view cannot be reached. A page with no running renderer reports zero, and the two
    // cases must stay distinct.
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
        // A page whose renderer has gone reports no address of its own. The address recorded when the page
        // navigated names it instead. Only a page that never navigated goes unnamed.
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
        if (_nativeHandle != IntPtr.Zero &&
            FromNativeHandle(_nativeHandle) == this)
        {
            ViewsByNativeHandle.Remove(_nativeHandle);
        }

        _onNativeCommit = null;

        _keepAliveCancellation.Cancel();
        _keepAliveCancellation.Dispose();

        _findSession = null;

        base.ReleaseResources();
    }

    // The macOS head keeps every WKWebView alive, so the native view leaks. Killing the renderer process alone
    // fails, because WebKit relaunches a renderer for the live native view. WKWebView's _close teardown SPI runs
    // once the control leaves the tree. _close ends the renderer and marks the native view closed, which stops
    // WebKit relaunching the renderer.
    protected override void CloseControl(Panel? container)
    {
        // An unresolved native view is resolved here only to close it. The handle stays in this method, out of the map.
        var nativeHandle = _nativeHandle;
        if (nativeHandle == IntPtr.Zero)
        {
            MacOSWebViewInterop.TryGetNativeWebViewHandle(_coreWebView2, out nativeHandle, out _);
        }

        // The dispose hook keeps a detached page loading, so the closing page is stopped here. Stopping works through
        // the managed control, so the page stops even when the teardown below is skipped.
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
