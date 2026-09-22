using Celbridge.Downloads;
using Celbridge.Localization;
using Celbridge.Logging;
using Microsoft.Web.WebView2.Core;

namespace Celbridge.WebHost.Platform;

/// <summary>
/// One WebView2 download, as the download service sees it. The service holds this for as long as the
/// transfer runs, which is what keeps the operation alive: WebView2 raises no further events for a
/// download operation whose wrapper has been collected, and an event subscription holds the handler
/// rather than the operation, so nothing else roots it.
/// </summary>
internal sealed class WebView2DownloadTransfer : IDownloadTransfer
{
    private readonly CoreWebView2DownloadOperation _downloadOperation;

    public WebView2DownloadTransfer(CoreWebView2DownloadOperation downloadOperation)
    {
        _downloadOperation = downloadOperation;
    }

    public void Cancel()
    {
        _downloadOperation.Cancel();
    }
}

/// <summary>
/// Drives the download service from WebView2's DownloadStarting, so every web surface downloads the same
/// way. The service decides where a download goes, and this relays the events and reports the outcome.
/// Chromium ends a navigation it turns into a download before it raises DownloadStarting, so
/// DownloadStarted follows that navigation's end, and it is raised for every download.
/// </summary>
internal sealed class WebView2DownloadHandler : IWebViewDownloadHandler
{
    private readonly ILogger<WebView2DownloadHandler> _logger;
    private readonly ILocalizerService _localizerService;
    private readonly IDownloadService _downloadService;
    private readonly CoreWebView2 _coreWebView2;

    public event EventHandler? DownloadStarted;

    private WebView2DownloadHandler(CoreWebView2 coreWebView2)
    {
        _logger = ServiceLocator.AcquireService<ILogger<WebView2DownloadHandler>>();
        _localizerService = ServiceLocator.AcquireService<ILocalizerService>();
        _downloadService = ServiceLocator.AcquireService<IDownloadService>();

        _coreWebView2 = coreWebView2;
    }

    /// <summary>
    /// Starts routing the WebView's downloads through the download service.
    /// </summary>
    public static WebView2DownloadHandler Attach(CoreWebView2 coreWebView2)
    {
        var handler = new WebView2DownloadHandler(coreWebView2);

        coreWebView2.DownloadStarting += handler.OnDownloadStarting;

        return handler;
    }

    public void Detach()
    {
        _coreWebView2.DownloadStarting -= OnDownloadStarting;
    }

    private async void OnDownloadStarting(CoreWebView2 sender, CoreWebView2DownloadStartingEventArgs args)
    {
        // WebView2 reads the args mutations only once the deferral completes. Without it, an await
        // mid-handler would let the runtime proceed with the original args before our overrides land.
        var deferral = args.GetDeferral();
        try
        {
            // The title bar's download badge is the download UI, so WebView2's own flyout is suppressed.
            args.Handled = true;

            var resultFilePath = args.ResultFilePath;
            if (string.IsNullOrEmpty(resultFilePath))
            {
                args.Cancel = true;
                return;
            }

            // Announced before the outcome is known, because the navigation this replaced is cancelled
            // whether or not the download goes on to start.
            DownloadStarted?.Invoke(this, EventArgs.Empty);

            var downloadOperation = args.DownloadOperation;
            var transfer = new WebView2DownloadTransfer(downloadOperation);

            var fileName = WebView2SuggestedName.Resolve(
                resultFilePath,
                downloadOperation.ContentDisposition,
                downloadOperation.Uri);

            var beginResult = await _downloadService.BeginAsync(fileName, downloadOperation.Uri, transfer);
            if (beginResult.IsFailure)
            {
                args.Cancel = true;
                _logger.LogError($"Failed to start a download. {beginResult.DiagnosticReport}");
                return;
            }
            var ticket = beginResult.Value;

            args.ResultFilePath = ticket.StagingPath;

            _downloadService.ReportProgress(
                ticket.Id,
                downloadOperation.BytesReceived,
                ReadTotalBytes(downloadOperation));

            downloadOperation.BytesReceivedChanged += (operation, _) =>
            {
                _downloadService.ReportProgress(ticket.Id, operation.BytesReceived, ReadTotalBytes(operation));
            };

            downloadOperation.StateChanged += async (operation, _) =>
            {
                // Async-void event handler: an escaping exception ends up on the synchronization
                // context's unhandled-exception channel, so a WebView-side failure is contained here.
                try
                {
                    if (operation.State == CoreWebView2DownloadState.Completed)
                    {
                        await _downloadService.CompleteAsync(ticket.Id);
                    }
                    else if (operation.State == CoreWebView2DownloadState.Interrupted)
                    {
                        var reason = DescribeInterruption(operation.InterruptReason);
                        await _downloadService.FailAsync(ticket.Id, reason);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Download state change handler failed");
                }
            };
        }
        catch (Exception ex)
        {
            args.Cancel = true;
            _logger.LogError(ex, "Download starting handler failed");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private static long? ReadTotalBytes(CoreWebView2DownloadOperation downloadOperation)
    {
        var totalBytes = downloadOperation.TotalBytesToReceive;

        return totalBytes > 0 ? totalBytes : null;
    }

    private string DescribeInterruption(CoreWebView2DownloadInterruptReason interruptReason)
    {
        var key = interruptReason == CoreWebView2DownloadInterruptReason.UserCanceled
            ? "Downloads_TransferCancelled"
            : "Downloads_TransferFailed";

        return _localizerService.GetString(key);
    }
}
