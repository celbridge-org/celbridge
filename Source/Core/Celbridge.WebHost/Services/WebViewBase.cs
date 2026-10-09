using Celbridge.Logging;
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

    private readonly ILogger _logger;
    private readonly DispatcherQueue? _dispatcherQueue;
    private readonly List<string> _documentStartScripts = new();

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

    protected WebViewBase(WebView2? control, ILogger logger)
    {
        Control = control;
        CoreWebView2 = control?.CoreWebView2;
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
    /// True once the view is disposed.
    /// </summary>
    internal bool IsDisposed => _isDisposed;

    /// <summary>
    /// Returns the view that owns a control, or null if the element is not a view's control.
    /// </summary>
    internal static WebViewBase? FromControl(DependencyObject element)
    {
        return element.GetValue(ControlMark.WebViewProperty) as WebViewBase;
    }

    /// <summary>
    /// The options the view was acquired with.
    /// </summary>
    protected WebViewOptions Options => _options;

    /// <summary>
    /// Applies the options the view was acquired with. The factory calls it once, when it hands the view out.
    /// </summary>
    internal void Configure(WebViewOptions options)
    {
        _options = options;

        if (Control is not null)
        {
            Control.DefaultBackgroundColor = options.BackgroundColor;
        }

        ApplyOptions(options);
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

    public event EventHandler? Closing;

    public event EventHandler<WebNavigationStartingEventArgs>? NavigationStarting;

    public event EventHandler<WebNavigationCompletedEventArgs>? NavigationCompleted;

    public event EventHandler? Attached;

    public event EventHandler? Detached;

    public event EventHandler<WebViewViewportSize>? ViewportSized;

    public event EventHandler? IsSizedChanged;

    public event EventHandler<string>? NewWindowRequested;

    public event EventHandler? HistoryChanged;

    public event EventHandler? DownloadStarted;

    public abstract event EventHandler<string>? NavigationCommitted;

    public async Task AddDocumentStartScriptAsync(string script)
    {
        ThrowIfDisposed();

        if (_documentStartScripts.Contains(script))
        {
            return;
        }

        _documentStartScripts.Add(script);

        await InstallDocumentStartScriptAsync(script);
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

    private void Control_Loaded(object sender, RoutedEventArgs e)
    {
        // A view reattached at its old size raises no SizeChanged, so the size is applied here.
        ApplyViewportSize();

        Attached?.Invoke(this, EventArgs.Empty);
    }

    private void Control_Unloaded(object sender, RoutedEventArgs e)
    {
        // A detached view's size is stale until it is laid out again.
        SetIsSized(false);

        Detached?.Invoke(this, EventArgs.Empty);
    }

    private void Control_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyViewportSize();
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

        ViewportSized?.Invoke(this, new WebViewViewportSize(width, height, isArranged));

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

    public abstract string Source { get; }

    public abstract void Navigate(string url);

    public abstract void LoadHtmlString(string html, string baseUrl);

    public abstract void MapVirtualHost(string hostName, string folderPath);

    public abstract bool CanGoBack { get; }

    public abstract bool CanGoForward { get; }

    public abstract void GoBack();

    public abstract void GoForward();

    public abstract Task StopAsync();

    public abstract Task StartFindAsync(string term, FindOptions options);

    public abstract void FindNext();

    public abstract void FindPrevious();

    public abstract void StopFind();

    public abstract WebViewHealth GetHealth();

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
    /// Installs a script that runs at document start on each later navigation.
    /// </summary>
    protected abstract Task InstallDocumentStartScriptAsync(string script);

    /// <summary>
    /// Runs a document-start script again after a navigation completes. A platform whose installed scripts run
    /// on every navigation can do nothing.
    /// </summary>
    protected abstract Task RerunDocumentStartScriptAsync(string script);

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
    /// Removes the subclass's event subscriptions and releases its native resources.
    /// </summary>
    protected abstract void ReleaseResources();

    /// <summary>
    /// Removes the control from its container and closes it, along with its native view.
    /// </summary>
    protected abstract void CloseControl(Panel? container);

    protected void RaiseNavigationStarting(WebNavigationStartingEventArgs args)
    {
        NavigationStarting?.Invoke(this, args);
    }

    protected void RaiseNavigationCompleted(WebNavigationCompletedEventArgs args)
    {
        NavigationCompleted?.Invoke(this, args);

        foreach (var script in _documentStartScripts.ToList())
        {
            _ = RerunScriptAfterNavigationAsync(script);
        }
    }

    private async Task RerunScriptAfterNavigationAsync(string script)
    {
        try
        {
            await RerunDocumentStartScriptAsync(script);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to run a document-start script again after a navigation of {Resource}", Resource);
        }
    }

    protected void RaiseNewWindowRequested(string uri)
    {
        NewWindowRequested?.Invoke(this, uri);
    }

    protected void RaiseHistoryChanged()
    {
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    protected void RaiseDownloadStarted()
    {
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
        Attached = null;
        Detached = null;
        ViewportSized = null;
        IsSizedChanged = null;
        NewWindowRequested = null;
        HistoryChanged = null;
        DownloadStarted = null;
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
