using Celbridge.Logging;
using Celbridge.Settings;
using Celbridge.WebHost.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Automation;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost;

/// <summary>
/// The shared part of every web view. A subclass for each head supplies the calls that reach the native view.
/// </summary>
public abstract class WebViewBase : IEditorWebView
{
    // A screenshot first waits about two frames. A page can report its content ready before it paints.
    private const int PaintBackstopMilliseconds = 50;

    // Shared assets, such as Bootstrap Icons, that every page can load.
    private const string SharedAssetsHostName = "shared.celbridge";
    private const string SharedAssetsFolderPath = "Celbridge.WebHost/Web";

    private readonly IWebViewPlatform _platform;
    private readonly ILogger _logger;
    private readonly DispatcherQueue? _dispatcherQueue;
    private readonly List<string> _documentStartScripts = new();

    // The scripts the platform could not install. They run after each navigation instead.
    private readonly List<string> _scriptsRunAfterNavigation = new();

    // Guards the resource and the accessible name. Tool calls read them on server threads, and a rename sets them
    // on the UI thread.
    private readonly object _identityLock = new();
    private ResourceKey _resource = ResourceKey.Empty;
    private string _accessibleName = string.Empty;

    private WebViewOptions _options = WebViewOptions.Default;
    private Panel? _container;
    private double _presentedWidth;
    private double _presentedHeight;
    private bool _isDisposed;

    private IWebViewDownloadHandler? _downloadHandler;
    private IDisposable? _navigationCommits;
    private EventHandler<string>? _navigationCommitted;

    // Created when the view is handed out, so a view waiting in the prewarm queue logs nothing.
    private WebViewLoadDiagnostics? _diagnostics;

    // Counts the navigations the view has started or heard of. A probe's verdict stands only if none started while
    // it ran.
    private int _navigationCount;

    // True from a reported navigation start until its completion. On the Skia heads a navigation made while the view
    // is detached may never report a start, so the owner's own navigations do not set it.
    private bool _isNavigationInFlight;

    // Where the latest navigation was heading, for the log when its page does not arrive.
    private string _navigationDestination = string.Empty;

    // Set when a download replaces the navigation in flight.
    private bool _isNavigationReplacedByDownload;

    protected WebViewBase(WebView2? control, IWebViewPlatform platform, ILogger logger)
    {
        Control = control;
        CoreWebView2 = control?.CoreWebView2;
        _platform = platform;
        _logger = logger;
        _dispatcherQueue = control?.DispatcherQueue;

        if (control is null)
        {
            return;
        }

        control.SetValue(ControlMark.WebViewProperty, this);

        control.Loaded += Control_Loaded;
        control.Unloaded += Control_Unloaded;
        control.SizeChanged += Control_SizeChanged;
        control.GotFocus += Control_GotFocus;

        if (CoreWebView2 is null)
        {
            return;
        }

        CoreWebView2.SetVirtualHostNameToFolderMapping(
            SharedAssetsHostName,
            SharedAssetsFolderPath,
            CoreWebView2HostResourceAccessKind.Allow);

        CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
        CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
        CoreWebView2.HistoryChanged += CoreWebView2_HistoryChanged;
        CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
        CoreWebView2.ProcessFailed += CoreWebView2_ProcessFailed;
        CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
    }

    /// <summary>
    /// The WebView2 control. Null only in tests.
    /// </summary>
    internal WebView2? Control { get; }

    /// <summary>
    /// The control's CoreWebView2. It is read once, so every caller gets the same object.
    /// </summary>
    internal CoreWebView2? CoreWebView2 { get; }

    /// <summary>
    /// What the view has observed about whether its page still works.
    /// </summary>
    internal WebViewHealthTracker Health { get; } = new();

    /// <summary>
    /// True once the view is disposed.
    /// </summary>
    internal bool IsDisposed => _isDisposed;

    /// <summary>
    /// Returns the view that owns a control, or null if the element is not a view's control.
    /// </summary>
    public static WebViewBase? FromControl(DependencyObject element)
    {
        return element.GetValue(ControlMark.WebViewProperty) as WebViewBase;
    }

    /// <summary>
    /// The options the view was acquired with.
    /// </summary>
    protected WebViewOptions Options => _options;

    /// <summary>
    /// The handler routing the page's downloads, once the view is configured.
    /// </summary>
    internal IWebViewDownloadHandler? DownloadHandler => _downloadHandler;

    /// <summary>
    /// Applies the options the view was acquired with. The factory calls it once, when it hands the view out.
    /// </summary>
    internal async Task ConfigureAsync(WebViewOptions options, IFeatureFlags featureFlags)
    {
        _options = options;

        _diagnostics = new WebViewLoadDiagnostics(this, featureFlags, _logger);

        if (Control is not null)
        {
            Control.DefaultBackgroundColor = options.BackgroundColor;
        }

        ApplyOptions(options);

        // Downloads go through the download service, so they land in the project.
        _downloadHandler = CreateDownloadHandler();
        _downloadHandler.DownloadStarted += DownloadHandler_DownloadStarted;

        // Installed before the owner's first navigation, so the first page reports its losses too.
        try
        {
            await AddDocumentStartScriptAsync(WebViewFocusLostScript.Source);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to install the focus-lost listener, so the page cannot report losing the keyboard");
        }
    }

    public ResourceKey Resource
    {
        get
        {
            lock (_identityLock)
            {
                return _resource;
            }
        }
    }

    public string AccessibleName
    {
        get
        {
            lock (_identityLock)
            {
                return ResolveAccessibleName();
            }
        }
    }

    public void SetResource(ResourceKey resource)
    {
        string previousAccessibleName;
        lock (_identityLock)
        {
            if (_resource == resource)
            {
                return;
            }

            previousAccessibleName = ResolveAccessibleName();
            _resource = resource;
        }

        ApplyIdentity(previousAccessibleName);
    }

    public void SetAccessibleName(string accessibleName)
    {
        string previousAccessibleName;
        lock (_identityLock)
        {
            if (_accessibleName == accessibleName)
            {
                return;
            }

            previousAccessibleName = ResolveAccessibleName();
            _accessibleName = accessibleName;
        }

        ApplyIdentity(previousAccessibleName);
    }

    // The accessible name, or the resource's name when none is set. The caller holds the identity lock.
    private string ResolveAccessibleName()
    {
        if (string.IsNullOrEmpty(_accessibleName))
        {
            return _resource.ResourceName;
        }

        return _accessibleName;
    }

    // The automation name is the accessible name, and the automation ID is the resource key.
    private void ApplyIdentity(string previousAccessibleName)
    {
        if (_isDisposed)
        {
            return;
        }

        if (Control is not null)
        {
            AutomationProperties.SetName(Control, AccessibleName);
            AutomationProperties.SetAutomationId(Control, Resource.ToString());
        }

        if (AccessibleName != previousAccessibleName)
        {
            OnAccessibleNameChanged();
        }
    }

    public bool SupportsVirtualHostMapping => _platform.SupportsVirtualHostMapping;

    public bool ProvidesBuiltInFind => _platform.ProvidesBuiltInFind;

    public bool CanSizeUnarrangedViewport => _platform.CanSizeUnarrangedViewport;

    public event EventHandler? Closing;

    public event EventHandler<WebNavigationStartingEventArgs>? NavigationStarting;

    public event EventHandler<WebNavigationCompletedEventArgs>? NavigationCompleted;

    public event EventHandler? IsSizedChanged;

    public event EventHandler<string>? NewWindowRequested;

    public event EventHandler? HistoryChanged;

    public event EventHandler? DownloadStarted;

    public event EventHandler? LoadedEmpty;

    public event EventHandler<string>? WebMessageReceived;

    public event EventHandler? FocusGained;

    public event EventHandler? FocusLost;

    public event EventHandler<string>? NavigationCommitted
    {
        add
        {
            _navigationCommitted += value;

            // Commits are observed only once a handler subscribes, so most views never observe them.
            _navigationCommits ??= ObserveNavigationCommits(OnNavigationCommitted);
        }
        remove
        {
            _navigationCommitted -= value;
        }
    }

    private void OnNavigationCommitted(string url)
    {
        _diagnostics?.LogNavigation("Navigation committed", url);

        _navigationCommitted?.Invoke(this, url);
    }

    public async Task AddDocumentStartScriptAsync(string script)
    {
        ThrowIfDisposed();

        if (_documentStartScripts.Contains(script))
        {
            return;
        }

        _documentStartScripts.Add(script);

        var installed = await InstallDocumentStartScriptAsync(script);
        if (!installed)
        {
            _scriptsRunAfterNavigation.Add(script);
        }
    }

    public Task<string> EvalAsync(string expression)
    {
        return RunOnUIThreadAsync(() => EvalScriptAsync(expression));
    }

    public Task ReloadAsync(bool clearCache)
    {
        return RunOnUIThreadAsync(async () =>
        {
            await ReloadPageAsync(clearCache);
            return true;
        });
    }

    public Task<ScreenshotData> CaptureScreenshotAsync(ScreenshotRequest request)
    {
        return RunOnUIThreadAsync(() => CaptureOnScreenAsync(request));
    }

    private async Task<ScreenshotData> CaptureOnScreenAsync(ScreenshotRequest request)
    {
        // A view that is off screen is not drawn, so a capture would never complete.
        if (!IsOnScreen())
        {
            throw new InvalidOperationException(
                "Screenshot requires the target document to be on screen. Its tab is not " +
                "the one its section is showing, or its area is hidden, so it is not drawn. " +
                "Bring it to the front with document_activate before calling webview_screenshot.");
        }

        var settleMilliseconds = PaintBackstopMilliseconds + request.SettleMs;
        if (settleMilliseconds > 0)
        {
            await Task.Delay(settleMilliseconds);
        }

        ThrowIfDisposed();

        if (!IsOnScreen())
        {
            throw new InvalidOperationException(
                "Screenshot target went off screen during the settle delay. " +
                "Bring the document tab to the front and retry.");
        }

        return await CaptureScreenshotCoreAsync(request);
    }

    // TabView unloads the tabs it is not showing. An unloaded view does not render.
    private bool IsOnScreen()
    {
        return Control is not null
            && Control.IsLoaded
            && Control.Visibility == Visibility.Visible
            && Control.ActualWidth > 0
            && Control.ActualHeight > 0;
    }

    public void AttachTo(Panel container)
    {
        if (_isDisposed)
        {
            return;
        }

        if (ReferenceEquals(container, _container))
        {
            return;
        }

        if (Control is not null)
        {
            _container?.Children.Remove(Control);
            container.Children.Add(Control);
        }

        _container = container;
    }

    public void SetPresentedSize(double width, double height)
    {
        if (width <= 0 ||
            height <= 0)
        {
            return;
        }

        _presentedWidth = width;
        _presentedHeight = height;

        ApplyViewportSize();
    }

    public bool IsSized { get; private set; }

    // Attach and detach are what a tab switch or a dock does to the surface, so both are logged with the state they
    // leave it in.
    private void Control_Loaded(object sender, RoutedEventArgs e)
    {
        OnAttached();

        // A view reattached at its old size raises no SizeChanged, so the size is applied here.
        ApplyViewportSize();

        var diagnostics = _diagnostics;
        if (diagnostics is null)
        {
            return;
        }

        _ = diagnostics.LogSurfaceAsync("WebView attached");

        // On the Skia heads a page that loaded while the view was detached raised no navigation events, so its load
        // was never probed. Attach is the first moment the view hears from it again.
        _ = ProbeContentAsync();
    }

    private void Control_Unloaded(object sender, RoutedEventArgs e)
    {
        // A detached view's size is stale until it is laid out again.
        SetIsSized(false);

        var diagnostics = _diagnostics;
        if (diagnostics is null)
        {
            return;
        }

        _ = diagnostics.LogSurfaceAsync("WebView detached");

        // Detach is raised before Uno takes the native view apart. The settled state is logged once that work has
        // run.
        _dispatcherQueue?.TryEnqueue(
            DispatcherQueuePriority.Low,
            () => { _ = diagnostics.LogSurfaceAsync("WebView detached, settled"); });
    }

    private void Control_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyViewportSize();
    }

    // Managed focus is the focus signal on the packaged Windows head, where it also fires for clicks on content
    // that raises no DOM focus event.
    private void Control_GotFocus(object sender, RoutedEventArgs e)
    {
        RaiseFocusGained();
    }

    /// <summary>
    /// Raises FocusGained for a focus signal the subclass observes.
    /// </summary>
    protected void RaiseFocusGained()
    {
        FocusGained?.Invoke(this, EventArgs.Empty);
    }

    // Sets the page's viewport size. After layout it uses the layout size. Before layout it uses the presented
    // size. IsSized tells the page whether its viewport is real.
    private void ApplyViewportSize()
    {
        if (_isDisposed ||
            Control is null)
        {
            return;
        }

        var isArranged = Control.ActualWidth > 0 && Control.ActualHeight > 0;

        var width = isArranged ? Control.ActualWidth : _presentedWidth;
        var height = isArranged ? Control.ActualHeight : _presentedHeight;
        if (width <= 0 ||
            height <= 0)
        {
            return;
        }

        // Set before IsSized changes, so the page measures the new size.
        var applied = SetNativeViewportSize(width, height);

        _diagnostics?.LogViewportSize(width, height, isArranged);

        // Before layout, a size the platform did not apply leaves the viewport a placeholder.
        if (!isArranged &&
            !applied)
        {
            return;
        }

        SetIsSized(true);
    }

    private void SetIsSized(bool isSized)
    {
        if (IsSized == isSized)
        {
            return;
        }

        IsSized = isSized;

        IsSizedChanged?.Invoke(this, EventArgs.Empty);
    }

    public string Source => CoreWebView2?.Source ?? string.Empty;

    public void Navigate(string url)
    {
        if (Control is null)
        {
            return;
        }

        NoteNavigation(url);

        Control.Source = new Uri(url, UriKind.Absolute);
    }

    public void LoadHtmlString(string html, string baseUrl)
    {
        NoteNavigation(baseUrl);

        LoadHtmlStringCore(html, baseUrl);
    }

    // Records a navigation the owner makes. On the Skia heads a view navigated while detached reports none of it,
    // so the view counts it here. Paired with the completion, the log line tells a page that never arrives from
    // one that arrived and failed.
    private void NoteNavigation(string url)
    {
        _navigationCount++;
        _navigationDestination = url;
        _isNavigationReplacedByDownload = false;

        _diagnostics?.LogNavigation("Navigating", url);
    }

    public void MapVirtualHost(string hostName, string folderPath)
    {
        CoreWebView2?.SetVirtualHostNameToFolderMapping(
            hostName,
            folderPath,
            CoreWebView2HostResourceAccessKind.Allow);
    }

    public bool CanGoBack => Control?.CanGoBack ?? false;

    public bool CanGoForward => Control?.CanGoForward ?? false;

    public void GoBack()
    {
        Control?.GoBack();
    }

    public void GoForward()
    {
        Control?.GoForward();
    }

    public WebViewHealth GetHealth()
    {
        return Health.GetHealth();
    }

    public abstract Task StopAsync();

    public abstract Task StartFindAsync(string term, FindOptions options);

    public abstract void FindNext();

    public abstract void FindPrevious();

    public abstract void StopFind();

    /// <summary>
    /// Gives the page keyboard focus, as a click inside the view does.
    /// </summary>
    internal virtual void FocusPage()
    {
        if (Control is null)
        {
            return;
        }

        // The platform refuses focus for a control it cannot focus, such as one in a hidden area, and the keyboard
        // then stays where it was. Nothing else records the refusal, so a page left without the keyboard is explained
        // here.
        if (!Control.Focus(FocusState.Programmatic))
        {
            _logger.LogDebug("The platform refused keyboard focus for the web view of {Resource}", Resource);
        }
    }

    /// <summary>
    /// Whether the platform still routes the keyboard to the view. The focus manager answers this, so it is only
    /// right once a focus change has finished: inside a focus event it still names the element focus is leaving.
    /// </summary>
    internal virtual bool HoldsKeyboard()
    {
        var xamlRoot = Control?.XamlRoot;
        if (xamlRoot is null)
        {
            return false;
        }

        var focusedElement = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(xamlRoot);

        return ReferenceEquals(focusedElement, Control);
    }

    /// <summary>
    /// Sends a native key-down event straight to the page, bypassing the managed key pipeline. Returns whether it
    /// was sent. False where the host never delivers keys to a page itself.
    /// </summary>
    internal virtual bool SendKeyDown(IntPtr nativeKeyEvent)
    {
        return false;
    }

    /// <summary>
    /// Sends a native key-down event to the page unless a web view has already received it. Returns whether it was
    /// sent.
    /// </summary>
    internal virtual bool ForwardKeyDown(IntPtr nativeKeyEvent)
    {
        return false;
    }

    /// <summary>
    /// Describes the native view behind the page for the log. Empty where the platform has nothing to report.
    /// </summary>
    internal virtual string DescribeNativeSurface()
    {
        return string.Empty;
    }

    /// <summary>
    /// Applies the options that need the native view.
    /// </summary>
    protected abstract void ApplyOptions(WebViewOptions options);

    /// <summary>
    /// Called when the accessible name changes.
    /// </summary>
    protected virtual void OnAccessibleNameChanged()
    {
    }

    /// <summary>
    /// Called when the control enters the visual tree.
    /// </summary>
    protected virtual void OnAttached()
    {
    }

    /// <summary>
    /// Loads an HTML string as a page whose origin is baseUrl.
    /// </summary>
    protected abstract void LoadHtmlStringCore(string html, string baseUrl);

    /// <summary>
    /// Installs a script that runs at document start on each later navigation. Returns false where the platform
    /// cannot, and the view then runs the script after each navigation instead.
    /// </summary>
    protected abstract Task<bool> InstallDocumentStartScriptAsync(string script);

    /// <summary>
    /// Sets the geometry the page reads as its viewport, and returns whether the platform applied it.
    /// </summary>
    protected abstract bool SetNativeViewportSize(double width, double height);

    /// <summary>
    /// Evaluates an expression on the UI thread.
    /// </summary>
    protected abstract Task<string> EvalScriptAsync(string expression);

    /// <summary>
    /// Reloads the page on the UI thread.
    /// </summary>
    protected abstract Task ReloadPageAsync(bool clearCache);

    /// <summary>
    /// Captures the page on the UI thread. The view is on screen when this is called.
    /// </summary>
    protected abstract Task<ScreenshotData> CaptureScreenshotCoreAsync(ScreenshotRequest request);

    /// <summary>
    /// Creates the handler that routes the page's downloads through the download service.
    /// </summary>
    protected abstract IWebViewDownloadHandler CreateDownloadHandler();

    /// <summary>
    /// Starts reporting each address the page commits to. Disposing the result stops it.
    /// </summary>
    protected abstract IDisposable ObserveNavigationCommits(NavigationCommitted onCommitted);

    /// <summary>
    /// Removes the view's event subscriptions and releases its native resources.
    /// </summary>
    protected virtual void ReleaseResources()
    {
        if (CoreWebView2 is not null)
        {
            CoreWebView2.NavigationStarting -= CoreWebView2_NavigationStarting;
            CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
            CoreWebView2.HistoryChanged -= CoreWebView2_HistoryChanged;
            CoreWebView2.NewWindowRequested -= CoreWebView2_NewWindowRequested;
            CoreWebView2.ProcessFailed -= CoreWebView2_ProcessFailed;
            CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
        }

        _navigationCommitted = null;
        _navigationCommits?.Dispose();
        _navigationCommits = null;

        if (_downloadHandler is not null)
        {
            _downloadHandler.DownloadStarted -= DownloadHandler_DownloadStarted;
            _downloadHandler.Detach();
            _downloadHandler = null;
        }
    }

    /// <summary>
    /// Removes the control from its container and closes it.
    /// </summary>
    protected virtual void CloseControl(Panel? container)
    {
        if (Control is null)
        {
            return;
        }

        container?.Children.Remove(Control);
        Control.Close();
    }

    internal static WebNavigationCompletedEventArgs CreateNavigationCompletedEventArgs(
        bool isSuccess,
        CoreWebView2WebErrorStatus status)
    {
        if (isSuccess)
        {
            return new WebNavigationCompletedEventArgs(WebNavigationResult.Succeeded, string.Empty);
        }

        var result = status switch
        {
            CoreWebView2WebErrorStatus.OperationCanceled => WebNavigationResult.Cancelled,
            CoreWebView2WebErrorStatus.ConnectionAborted => WebNavigationResult.Aborted,
            _ => WebNavigationResult.Failed
        };

        return new WebNavigationCompletedEventArgs(result, status.ToString());
    }

    private void CoreWebView2_NavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (OnNavigationStarting(args.Uri ?? string.Empty))
        {
            args.Cancel = true;
        }
    }

    /// <summary>
    /// Raises NavigationStarting for a navigation the platform reports, and returns whether a handler cancelled it.
    /// </summary>
    internal bool OnNavigationStarting(string uri)
    {
        // A navigation can give the page a new renderer, so the process reading starts again from here. Only a
        // change with no navigation behind it is reported.
        Health.RecordNavigation(uri);

        _navigationCount++;
        _isNavigationReplacedByDownload = false;
        if (uri.Length > 0)
        {
            _navigationDestination = uri;
        }

        // A start with no Navigating line before it is the page navigating on its own, as a redirect does.
        _diagnostics?.LogNavigation("Navigation starting", uri);

        var navigationArgs = new WebNavigationStartingEventArgs(uri);

        NavigationStarting?.Invoke(this, navigationArgs);

        if (navigationArgs.Cancel)
        {
            return true;
        }

        _isNavigationInFlight = true;

        return false;
    }

    private void CoreWebView2_NavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        OnNavigationCompleted(CreateNavigationCompletedEventArgs(args.IsSuccess, args.WebErrorStatus));
    }

    /// <summary>
    /// Raises NavigationCompleted for a navigation the platform reports ended, then probes a page that loaded.
    /// </summary>
    internal void OnNavigationCompleted(WebNavigationCompletedEventArgs completion)
    {
        // A navigation that became a download leaves the old page on screen, so it was abandoned rather than failed.
        // WebKit announces the download first and then ends the navigation with a failure. Chromium ends it as
        // aborted before announcing the download.
        if (_isNavigationReplacedByDownload
            && completion.Result == WebNavigationResult.Failed)
        {
            completion = completion with { Result = WebNavigationResult.Aborted };
        }

        _isNavigationReplacedByDownload = false;
        _isNavigationInFlight = false;

        if (completion.Result == WebNavigationResult.Succeeded)
        {
            _diagnostics?.LogNavigation("Navigation completed", Source);
        }
        else
        {
            // Logged by where it was heading, since the page that did not arrive has no address of its own.
            _diagnostics?.LogNavigationNotLoaded(_navigationDestination, completion);
        }

        NavigationCompleted?.Invoke(this, completion);

        foreach (var script in _scriptsRunAfterNavigation.ToList())
        {
            _ = RunScriptAfterNavigationAsync(script);
        }

        // Probed after the owner has heard of the completion.
        if (completion.Result == WebNavigationResult.Succeeded)
        {
            _ = ProbeContentAsync();
        }
    }

    // Probes the page, logs what it holds, and raises LoadedEmpty for an empty document. A verdict about a page that
    // a navigation is leaving is dropped.
    private async Task ProbeContentAsync()
    {
        var diagnostics = _diagnostics;
        if (diagnostics is null
            || _isDisposed
            || _isNavigationInFlight)
        {
            return;
        }

        var navigationCount = _navigationCount;

        var probe = await diagnostics.ProbeAsync();
        if (probe is null
            || _isDisposed
            || _isNavigationInFlight
            || _navigationCount != navigationCount)
        {
            return;
        }

        diagnostics.LogProbe(probe);

        if (probe.IsEmpty)
        {
            LoadedEmpty?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task RunScriptAfterNavigationAsync(string script)
    {
        try
        {
            if (CoreWebView2 is not null)
            {
                await CoreWebView2.ExecuteScriptAsync(script);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to run a document-start script after a navigation of {Resource}", Resource);
        }
    }

    private void CoreWebView2_HistoryChanged(CoreWebView2 sender, object args)
    {
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    // No new window ever opens. The owner can follow the address in the same view.
    private void CoreWebView2_NewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;

        var uri = args.Uri;
        if (string.IsNullOrEmpty(uri))
        {
            return;
        }

        // Raised inside a native callback, where an exception would end the process.
        try
        {
            NewWindowRequested?.Invoke(this, uri);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle a new window requested by the page of {Resource}", Resource);
        }
    }

    // Only the packaged Windows head raises ProcessFailed. The macOS view reads its renderer's process itself.
    private void CoreWebView2_ProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs args)
    {
        Health.RecordProcessFailed();

        _logger.LogError(
            "WebView ProcessFailed for {Resource}: Kind={Kind}, Reason={Reason}, ExitCode={ExitCode}",
            Resource, args.ProcessFailedKind, args.Reason, args.ExitCode);
    }

    // Raised inside a native callback, where an exception would end the process.
    private void CoreWebView2_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            // Read as JSON rather than through TryGetWebMessageAsString, which throws on the macOS head, where a
            // message arrives as JSON rather than a string.
            var message = args.WebMessageAsJson;
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            if (IsFocusLostReport(message))
            {
                FocusLost?.Invoke(this, EventArgs.Empty);
            }

            WebMessageReceived?.Invoke(this, message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle a message from the page of {Resource}", Resource);
        }
    }

    // Every message the page sends arrives here, including editor content, so the method name only pre-filters
    // which messages are worth parsing.
    private static bool IsFocusLostReport(string message)
    {
        return message.Contains(InputRpcMethods.FocusLost, StringComparison.Ordinal)
            && WebMessageEnvelope.TryRead(message, InputRpcMethods.FocusLost) is not null;
    }

    private void DownloadHandler_DownloadStarted(object? sender, EventArgs e)
    {
        // A navigation that started while the view was detached reported no start, so this is not gated on one being
        // known to be in flight.
        _isNavigationReplacedByDownload = true;

        DownloadStarted?.Invoke(this, EventArgs.Empty);
    }

    private Task<T> RunOnUIThreadAsync<T>(Func<Task<T>> operation)
    {
        if (_dispatcherQueue is null ||
            _dispatcherQueue.HasThreadAccess)
        {
            if (_isDisposed)
            {
                return Task.FromException<T>(CreateDisposedException());
            }

            return operation();
        }

        var completionSource = new TaskCompletionSource<T>();

        var enqueued = _dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                // The view may have been disposed while this call waited in the queue.
                ThrowIfDisposed();

                var result = await operation();
                completionSource.TrySetResult(result);
            }
            catch (Exception ex)
            {
                completionSource.TrySetException(ex);
            }
        });

        if (!enqueued)
        {
            completionSource.TrySetException(new InvalidOperationException("Failed to dispatch a web view call to the UI thread"));
        }

        return completionSource.Task;
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw CreateDisposedException();
        }
    }

    private ObjectDisposedException CreateDisposedException()
    {
        return new ObjectDisposedException(GetType().Name, $"The web view for '{Resource}' has closed");
    }

    // Each step runs on its own, so a failure in one never skips the native close.
    private void RunTeardownStep(string description, Action step)
    {
        try
        {
            step();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to {Step} while closing the web view for {Resource}", description, Resource);
        }
    }

    private void RaiseClosing()
    {
        var closing = Closing;
        if (closing is null)
        {
            return;
        }

        foreach (var handler in closing.GetInvocationList().Cast<EventHandler>())
        {
            RunTeardownStep("notify a service", () => handler(this, EventArgs.Empty));
        }
    }

    private void ClearEventHandlers()
    {
        Closing = null;
        NavigationStarting = null;
        NavigationCompleted = null;
        IsSizedChanged = null;
        NewWindowRequested = null;
        HistoryChanged = null;
        DownloadStarted = null;
        LoadedEmpty = null;
        WebMessageReceived = null;
        FocusGained = null;
        FocusLost = null;
    }

    private void ReleaseControl()
    {
        if (Control is null)
        {
            return;
        }

        Control.Loaded -= Control_Loaded;
        Control.Unloaded -= Control_Unloaded;
        Control.SizeChanged -= Control_SizeChanged;
        Control.GotFocus -= Control_GotFocus;

        Control.ClearValue(ControlMark.WebViewProperty);
    }

    // Registered on first use. A view without a control, as in a test, never registers it.
    private static class ControlMark
    {
        public static readonly DependencyProperty WebViewProperty = DependencyProperty.RegisterAttached(
            "WebView",
            typeof(object),
            typeof(WebViewBase),
            new PropertyMetadata(null));
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        // Disposal runs on the UI thread, so no other UI work interleaves with it.
        if (_dispatcherQueue is not null &&
            !_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(Dispose);
            return;
        }

        _isDisposed = true;

        // Services drop the view here, before anything is released.
        RaiseClosing();

        ClearEventHandlers();

        RunTeardownStep("release its control", ReleaseControl);
        RunTeardownStep("release its native resources", ReleaseResources);
        RunTeardownStep("close its control", () => CloseControl(_container));

        _container = null;
    }
}
