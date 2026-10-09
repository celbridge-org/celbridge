using Celbridge.Logging;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost;

/// <summary>
/// A web view that reaches its native view through the platform's IWebViewAdapter.
/// </summary>
internal sealed class AdapterWebView : WebViewBase
{
    private readonly WebView2 _control;
    private readonly CoreWebView2 _coreWebView2;
    private readonly IWebViewAdapter _webViewAdapter;
    private readonly ILogger<AdapterWebView> _logger;

    private IWebViewDownloadHandler? _downloadHandler;
    private IDisposable? _navigationCommits;
    private EventHandler<string>? _navigationCommitted;

    // Counts ProcessFailed events. Only the packaged Windows head raises them. The Skia heads report a dead
    // renderer through the adapter's page health.
    private int _processFailures;

    public AdapterWebView(WebView2 control, IWebViewAdapter webViewAdapter, ILogger<AdapterWebView> logger)
        : base(control, logger)
    {
        _control = control;
        _coreWebView2 = CoreWebView2!;
        _webViewAdapter = webViewAdapter;
        _logger = logger;

        _coreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
        _coreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
        _coreWebView2.HistoryChanged += CoreWebView2_HistoryChanged;
        _coreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
        _coreWebView2.ProcessFailed += CoreWebView2_ProcessFailed;
    }

    protected override void ApplyOptions(WebViewOptions options)
    {
        _webViewAdapter.SetDevToolsEnabled(_coreWebView2, options.IsDevToolsEnabled, AccessibleName);
        _webViewAdapter.SetZoomControlEnabled(_coreWebView2, options.IsZoomEnabled);

        if (!string.IsNullOrEmpty(options.UserAgentToken))
        {
            _webViewAdapter.SetApplicationUserAgent(_coreWebView2, options.UserAgentToken);
        }

        // Downloads go through the download service, so they land in the project.
        _downloadHandler = _webViewAdapter.AttachDownloadHandler(_coreWebView2);
        _downloadHandler.DownloadStarted += DownloadHandler_DownloadStarted;
    }

    // The developer tools list the page under its accessible name.
    protected override void OnAccessibleNameChanged()
    {
        if (!Options.IsDevToolsEnabled)
        {
            return;
        }

        _webViewAdapter.SetDevToolsEnabled(_coreWebView2, enabled: true, AccessibleName);
    }

    public override event EventHandler<string>? NavigationCommitted
    {
        add
        {
            _navigationCommitted += value;

            // Commits are observed only once a handler subscribes, so most views never observe them.
            _navigationCommits ??= _webViewAdapter.ObserveNavigationCommits(_coreWebView2, OnNavigationCommitted);
        }
        remove
        {
            _navigationCommitted -= value;
        }
    }

    private void OnNavigationCommitted(string url)
    {
        _navigationCommitted?.Invoke(this, url);
    }

    public override string Source => _coreWebView2.Source;

    public override void Navigate(string url)
    {
        _control.Source = new Uri(url, UriKind.Absolute);
    }

    public override void LoadHtmlString(string html, string baseUrl)
    {
        _webViewAdapter.LoadHtmlString(_coreWebView2, html, baseUrl);
    }

    public override void MapVirtualHost(string hostName, string folderPath)
    {
        _coreWebView2.SetVirtualHostNameToFolderMapping(
            hostName,
            folderPath,
            CoreWebView2HostResourceAccessKind.Allow);
    }

    public override bool CanGoBack => _control.CanGoBack;

    public override bool CanGoForward => _control.CanGoForward;

    public override void GoBack()
    {
        _control.GoBack();
    }

    public override void GoForward()
    {
        _control.GoForward();
    }

    public override async Task StopAsync()
    {
        await _webViewAdapter.StopAsync(_coreWebView2);
    }

    public override async Task StartFindAsync(string term, FindOptions options)
    {
        await _webViewAdapter.StartFindAsync(_coreWebView2, term, options);
    }

    public override void FindNext()
    {
        _webViewAdapter.FindNext(_coreWebView2);
    }

    public override void FindPrevious()
    {
        _webViewAdapter.FindPrevious(_coreWebView2);
    }

    public override void StopFind()
    {
        _webViewAdapter.StopFind(_coreWebView2);
    }

    // Adds the process failures this view counted to the adapter's page health.
    public override WebViewHealth GetHealth()
    {
        var pageHealth = _webViewAdapter.GetPageHealth(_coreWebView2);

        return pageHealth with { ProcessFailures = pageHealth.ProcessFailures + _processFailures };
    }

    protected override async Task InstallDocumentStartScriptAsync(string script)
    {
        await _webViewAdapter.InstallDocumentStartScriptAsync(_coreWebView2, script);
    }

    protected override async Task RerunDocumentStartScriptAsync(string script)
    {
        await _webViewAdapter.ReinjectDocumentStartScriptAsync(_coreWebView2, script);
    }

    protected override bool SetNativeViewportSize(double width, double height)
    {
        return _webViewAdapter.SetViewportSize(_control, width, height);
    }

    protected override async Task<string> EvalScriptAsync(string expression)
    {
        return await _webViewAdapter.EvalAsync(_coreWebView2, expression);
    }

    protected override async Task ReloadPageAsync(bool clearCache)
    {
        await _webViewAdapter.ReloadAsync(_coreWebView2, clearCache);
    }

    protected override async Task<ScreenshotData> CaptureScreenshotCoreAsync(ScreenshotRequest request)
    {
        return await _webViewAdapter.CaptureScreenshotAsync(_control, request);
    }

    private void CoreWebView2_NavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        var navigationArgs = new WebNavigationStartingEventArgs(args.Uri ?? string.Empty);

        RaiseNavigationStarting(navigationArgs);

        if (navigationArgs.Cancel)
        {
            args.Cancel = true;
        }
    }

    private void CoreWebView2_NavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        RaiseNavigationCompleted(args.IsSuccess, args.WebErrorStatus);
    }

    private void CoreWebView2_HistoryChanged(CoreWebView2 sender, object args)
    {
        RaiseHistoryChanged();
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
            RaiseNewWindowRequested(uri);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle a new window requested by the page of {Resource}", Resource);
        }
    }

    private void CoreWebView2_ProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs args)
    {
        _processFailures++;

        _logger.LogError(
            "WebView ProcessFailed for {Resource}: Kind={Kind}, Reason={Reason}, ExitCode={ExitCode}",
            Resource, args.ProcessFailedKind, args.Reason, args.ExitCode);
    }

    private void DownloadHandler_DownloadStarted(object? sender, EventArgs e)
    {
        RaiseDownloadStarted();
    }

    protected override void ReleaseResources()
    {
        _coreWebView2.NavigationStarting -= CoreWebView2_NavigationStarting;
        _coreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
        _coreWebView2.HistoryChanged -= CoreWebView2_HistoryChanged;
        _coreWebView2.NewWindowRequested -= CoreWebView2_NewWindowRequested;
        _coreWebView2.ProcessFailed -= CoreWebView2_ProcessFailed;

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

    protected override void CloseControl(Panel? container)
    {
        _webViewAdapter.CloseWebView(_control, container);
    }
}
