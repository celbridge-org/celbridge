using Celbridge.Downloads;
using Celbridge.Localization;
using Celbridge.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost.Platform;

/// <summary>
/// Drives the download service from WebKit on the macOS Skia head, where WebView2's DownloadStarting never
/// fires. The native download delegate reports a download's destination request, finish and failure, and
/// its progress is read while it runs. The service decides where a download goes, and this relays the
/// signals and reports the outcome. One router serves every web view, because the native hooks are
/// process-wide, and all of it runs on the main thread, where WebKit calls back.
/// </summary>
internal sealed class MacOSWebViewDownloadRouter : IMacOSDownloadListener
{
    // WebKit raises nothing as a download progresses, so its progress is read on a timer. The service
    // publishes at most four reports a second, and reading a little faster than that keeps each one current.
    private static readonly TimeSpan ProgressReadInterval = TimeSpan.FromMilliseconds(200);

    private readonly ILogger<MacOSWebViewDownloadRouter> _logger;
    private readonly ILocalizerService _localizerService;
    private readonly IDownloadService _downloadService;
    private readonly DispatcherQueue _dispatcherQueue;

    // The surfaces routing their downloads, keyed by the native web view behind each.
    private readonly Dictionary<IntPtr, DownloadHandler> _handlers = new();

    // The downloads WebKit is running, keyed by the native download, from the moment WebKit asks where to
    // write one until it ends.
    private readonly Dictionary<IntPtr, DownloadTransfer> _transfers = new();

    private DispatcherQueueTimer? _progressTimer;

    public MacOSWebViewDownloadRouter()
    {
        _logger = ServiceLocator.AcquireService<ILogger<MacOSWebViewDownloadRouter>>();
        _localizerService = ServiceLocator.AcquireService<ILocalizerService>();
        _downloadService = ServiceLocator.AcquireService<IDownloadService>();
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    /// <summary>
    /// Starts routing the web view's downloads through the download service. A web view whose native view
    /// cannot be reached gets a handler that routes nothing, so its downloads keep WebKit's own handling.
    /// </summary>
    public IWebViewDownloadHandler Attach(CoreWebView2 coreWebView2)
    {
        if (!MacOSWebViewInterop.TryGetNativeWebViewHandle(coreWebView2, out var webView, out var detail))
        {
            _logger.LogWarning($"Downloads from a web view will not reach the project: its native view could not be resolved ({detail})");
            return new DownloadHandler(this, IntPtr.Zero);
        }

        if (!MacOSWebViewInterop.RouteDownloads(webView, this, out var routeDetail))
        {
            _logger.LogWarning($"Downloads from a web view will not reach the project: {routeDetail}");
            return new DownloadHandler(this, IntPtr.Zero);
        }

        var handler = new DownloadHandler(this, webView);
        _handlers[webView] = handler;

        return handler;
    }

    public bool IsRoutingDownloads(IntPtr webView)
    {
        return _handlers.ContainsKey(webView);
    }

    public MacNavigationResponsePolicy DecideNavigationResponse(IntPtr webView, MacNavigationResponse response)
    {
        if (!_handlers.TryGetValue(webView, out var handler))
        {
            return response.CanShowMimeType
                ? MacNavigationResponsePolicy.Allow
                : MacNavigationResponsePolicy.Cancel;
        }

        var policy = DecideResponsePolicy(response.CanShowMimeType, response.ContentDisposition);

        // Announced now, before WebKit ends the navigation the download replaces, which it reports as a
        // failure no different from a page that could not be loaded.
        if (policy == MacNavigationResponsePolicy.Download &&
            response.IsForMainFrame)
        {
            try
            {
                handler.RaiseDownloadStarted();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Download started handler failed");
            }
        }

        return policy;
    }

    public void OnDownloadDestinationRequested(IntPtr download, string suggestedFileName, string sourceUrl)
    {
        var transfer = new DownloadTransfer(this, download);
        _transfers[download] = transfer;

        _ = BeginAsync(transfer, suggestedFileName, sourceUrl);
    }

    public void OnDownloadFinished(IntPtr download)
    {
        if (!_transfers.Remove(download, out var transfer) ||
            transfer.IsSettled)
        {
            return;
        }

        transfer.IsSettled = true;

        // The last reading, taken while the download can still be asked, so the row settles on the size
        // the file turned out to be.
        ReportProgress(transfer);

        _ = CompleteAsync(transfer.DownloadId);
    }

    public void OnDownloadFailed(IntPtr download, bool isCancelled, string description)
    {
        if (!_transfers.Remove(download, out var transfer) ||
            transfer.IsSettled)
        {
            return;
        }

        transfer.IsSettled = true;

        // A download WebKit stopped is a cancellation, not a failure. One stopped from the download list
        // has already been recorded as canceled, and WebKit's report of it never arrives here.
        var reason = string.Empty;
        if (!isCancelled)
        {
            reason = _localizerService.GetString("Downloads_TransferFailed");
            _logger.LogWarning($"A download failed: {description}");
        }

        if (transfer.DownloadId == 0)
        {
            // The service is still reserving the destination, and settles the row once it has.
            transfer.IsCanceled = isCancelled;
            transfer.FailureReason = reason;
            return;
        }

        if (isCancelled)
        {
            _ = RecordCancellationAsync(transfer.DownloadId);
            return;
        }

        _ = FailAsync(transfer.DownloadId, reason);
    }

    /// <summary>
    /// Returns what WebKit does with a navigation's response. A response the server marks as an attachment,
    /// or one WebKit cannot display, is downloaded, as a browser does. Everything else is displayed.
    /// </summary>
    internal static MacNavigationResponsePolicy DecideResponsePolicy(bool canShowMimeType, string contentDisposition)
    {
        if (!canShowMimeType ||
            IsAttachment(contentDisposition))
        {
            return MacNavigationResponsePolicy.Download;
        }

        return MacNavigationResponsePolicy.Allow;
    }

    /// <summary>
    /// Returns whether a Content-Disposition header marks its response as an attachment. Only the
    /// disposition type before the first parameter counts, which is how WebKit reads the header.
    /// </summary>
    internal static bool IsAttachment(string contentDisposition)
    {
        if (string.IsNullOrEmpty(contentDisposition))
        {
            return false;
        }

        var parameterStart = contentDisposition.IndexOf(';');
        var dispositionType = parameterStart < 0
            ? contentDisposition
            : contentDisposition.Substring(0, parameterStart);

        return string.Equals(dispositionType.Trim(), "attachment", StringComparison.OrdinalIgnoreCase);
    }

    private void Detach(DownloadHandler handler)
    {
        if (handler.WebView == IntPtr.Zero)
        {
            return;
        }

        // Only the handler still registered for the view, so a late detach cannot unroute a view another
        // surface has attached since.
        if (_handlers.TryGetValue(handler.WebView, out var registeredHandler) &&
            registeredHandler == handler)
        {
            _handlers.Remove(handler.WebView);
        }
    }

    private async Task BeginAsync(DownloadTransfer transfer, string suggestedFileName, string sourceUrl)
    {
        Result<DownloadTicket> beginResult;
        try
        {
            beginResult = await _downloadService.BeginAsync(suggestedFileName, sourceUrl, transfer);
        }
        catch (Exception ex)
        {
            beginResult = Result<DownloadTicket>.Fail($"Failed to start the download of '{suggestedFileName}'")
                .WithException(ex);
        }

        RunOnMainThread(() => OnBeginCompleted(transfer, beginResult));
    }

    private void OnBeginCompleted(DownloadTransfer transfer, Result<DownloadTicket> beginResult)
    {
        if (beginResult.IsFailure)
        {
            _transfers.Remove(transfer.Download);
            transfer.IsSettled = true;

            _logger.LogError($"Failed to start a download. {beginResult.DiagnosticReport}");

            MacOSWebViewInterop.ProvideDownloadDestination(transfer.Download, null);
            return;
        }
        var ticket = beginResult.Value;

        transfer.DownloadId = ticket.Id;

        if (transfer.IsSettled)
        {
            // WebKit gave up on the download, or it was stopped, while its destination was being reserved.
            MacOSWebViewInterop.ProvideDownloadDestination(transfer.Download, null);

            if (transfer.IsCanceled)
            {
                _ = RecordCancellationAsync(ticket.Id);
            }
            else if (!string.IsNullOrEmpty(transfer.FailureReason))
            {
                _ = FailAsync(ticket.Id, transfer.FailureReason);
            }
            return;
        }

        MacOSWebViewInterop.ProvideDownloadDestination(transfer.Download, ticket.StagingPath);

        StartReadingProgress();
    }

    private async Task CompleteAsync(long downloadId)
    {
        try
        {
            await _downloadService.CompleteAsync(downloadId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete a download");
        }
    }

    private async Task FailAsync(long downloadId, string reason)
    {
        try
        {
            await _downloadService.FailAsync(downloadId, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record a failed download");
        }
    }

    private async Task RecordCancellationAsync(long downloadId)
    {
        try
        {
            await _downloadService.ReportCanceledAsync(downloadId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record a canceled download");
        }
    }

    // Called by the service, which settles the download itself, so this only has to stop WebKit.
    private void Cancel(DownloadTransfer transfer)
    {
        RunOnMainThread(() =>
        {
            if (transfer.IsSettled)
            {
                return;
            }

            transfer.IsSettled = true;
            _transfers.Remove(transfer.Download);

            MacOSWebViewInterop.CancelDownload(transfer.Download);
        });
    }

    private void StartReadingProgress()
    {
        if (_progressTimer is null)
        {
            _progressTimer = _dispatcherQueue.CreateTimer();
            _progressTimer.Interval = ProgressReadInterval;
            _progressTimer.Tick += ProgressTimer_Tick;
        }

        if (!_progressTimer.IsRunning)
        {
            _progressTimer.Start();
        }
    }

    private void ProgressTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (_transfers.Count == 0)
        {
            sender.Stop();
            return;
        }

        var transfers = _transfers.Values.ToList();
        foreach (var transfer in transfers)
        {
            // A download still waiting on its destination has no record to report against yet.
            if (transfer.DownloadId == 0)
            {
                continue;
            }

            ReportProgress(transfer);
        }
    }

    private void ReportProgress(DownloadTransfer transfer)
    {
        var progress = MacOSWebViewInterop.GetDownloadProgress(transfer.Download);

        long? totalBytes = progress.TotalBytes > 0
            ? progress.TotalBytes
            : null;

        _downloadService.ReportProgress(transfer.DownloadId, progress.BytesReceived, totalBytes);
    }

    private void RunOnMainThread(Action action)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        if (!_dispatcherQueue.TryEnqueue(() => action()))
        {
            _logger.LogWarning("Could not return to the main thread to drive a download");
        }
    }

    private sealed class DownloadHandler : IWebViewDownloadHandler
    {
        private readonly MacOSWebViewDownloadRouter _router;

        public DownloadHandler(MacOSWebViewDownloadRouter router, IntPtr webView)
        {
            _router = router;
            WebView = webView;
        }

        // Zero for a handler that routes nothing.
        public IntPtr WebView { get; }

        public event EventHandler? DownloadStarted;

        public void RaiseDownloadStarted()
        {
            DownloadStarted?.Invoke(this, EventArgs.Empty);
        }

        public void Detach()
        {
            _router.Detach(this);
        }
    }

    // One download WebKit is running, as the download service sees it.
    private sealed class DownloadTransfer : IDownloadTransfer
    {
        private readonly MacOSWebViewDownloadRouter _router;

        public DownloadTransfer(MacOSWebViewDownloadRouter router, IntPtr download)
        {
            _router = router;
            Download = download;
        }

        public IntPtr Download { get; }

        // Zero until the service has reserved the download its destination.
        public long DownloadId { get; set; }

        // Set once the download has ended or been stopped, after which WebKit is not asked about it again.
        public bool IsSettled { get; set; }

        // How WebKit ended the download while its destination was still being reserved: stopped, or failed for
        // the reason given.
        public bool IsCanceled { get; set; }
        public string FailureReason { get; set; } = string.Empty;

        public void Cancel()
        {
            _router.Cancel(this);
        }
    }
}
