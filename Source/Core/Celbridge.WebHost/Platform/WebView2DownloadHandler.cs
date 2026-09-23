using Celbridge.Downloads;
using Celbridge.Localization;
using Celbridge.Logging;
using Microsoft.Web.WebView2.Core;

using Path = System.IO.Path;

namespace Celbridge.WebHost.Platform;

/// <summary>
/// One WebView2 download, as the download service sees it. The service holds this for as long as the
/// transfer runs, which is what keeps the operation alive: WebView2 raises no further events for a
/// download operation whose wrapper has been collected, and an event subscription holds the handler
/// rather than the operation, so nothing else roots it.
/// </summary>
internal sealed class WebView2DownloadTransfer : IDownloadTransfer
{
    public WebView2DownloadTransfer(CoreWebView2DownloadOperation downloadOperation)
    {
        Operation = downloadOperation;
    }

    /// <summary>
    /// The operation for the attempt at the download that is running. Chromium retries a download whose
    /// transfer broke off, and each retry replaces it, so a cancel reaches the attempt under way.
    /// </summary>
    public CoreWebView2DownloadOperation Operation { get; set; }

    public void Cancel()
    {
        Operation.Cancel();
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
    // A download this surface has handed to the service and not yet seen settle.
    private sealed class TrackedDownload
    {
        public TrackedDownload(long downloadId, string stagingPath, WebView2DownloadTransfer transfer)
        {
            DownloadId = downloadId;
            StagingPath = stagingPath;
            Transfer = transfer;
        }

        public long DownloadId { get; }
        public string StagingPath { get; }
        public WebView2DownloadTransfer Transfer { get; }

        // Stops relaying the attempt under way, and null once nothing is being relayed.
        public Action? StopObserving { get; set; }
    }

    private readonly ILogger<WebView2DownloadHandler> _logger;
    private readonly ILocalizerService _localizerService;
    private readonly IDownloadService _downloadService;
    private readonly CoreWebView2 _coreWebView2;

    // Chromium retries a download whose transfer broke off, and WebView2 raises DownloadStarting again for
    // each retry, so the downloads under way are kept to recognize a retry of one of them.
    private readonly List<TrackedDownload> _trackedDownloads = new();

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

        handler.ApplyDefaultDownloadFolder();

        coreWebView2.DownloadStarting += handler.OnDownloadStarting;

        return handler;
    }

    // WebView2 writes a download to its default download folder unless the user names another in a Save As
    // dialog, so pointing that folder at the project's downloads folder is what makes a path anywhere else
    // the user's own choice rather than a folder nobody picked. It is also the folder that dialog then
    // opens on, which is where a file downloaded from a project's page belongs. Applied per web view,
    // because the project's downloads folder can change between one and the next.
    private void ApplyDefaultDownloadFolder()
    {
        var folderResult = _downloadService.GetDestinationFolderPath();
        if (folderResult.IsFailure)
        {
            return;
        }

        try
        {
            // The folder need not exist: WebView2 creates it when a download first needs it.
            _coreWebView2.Profile.DefaultDownloadFolderPath = folderResult.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to point the default download folder at the project");
        }
    }

    public void Detach()
    {
        _coreWebView2.DownloadStarting -= OnDownloadStarting;

        // A transfer belongs to the web view that started it: the download operation raises no further
        // events once that web view is closed, so a download still running here would report nothing ever
        // again and sit in the list as though it were still going. Only this web view's downloads are
        // stopped, since the handler tracks no others.
        var abandonedDownloads = _trackedDownloads.ToArray();
        _trackedDownloads.Clear();

        if (abandonedDownloads.Length == 0)
        {
            return;
        }

        var reason = _localizerService.GetString("Downloads_TransferAbandoned");

        foreach (var abandonedDownload in abandonedDownloads)
        {
            // Stopped being relayed before the service stops it, so the cancellation the operation reports
            // does not settle the row over the reason given here.
            StopObserving(abandonedDownload);

            _logger.LogDebug($"Download {abandonedDownload.DownloadId} abandoned with its web view");

            _ = _downloadService.AbandonAsync(abandonedDownload.DownloadId, reason);
        }
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

            var downloadOperation = args.DownloadOperation;

            // Asked before the destination is judged, because a retry is offered the staging path this
            // handler named for the attempt it carries on from, which reads as a path outside the project.
            var retriedDownload = FindRetriedDownload(downloadOperation);
            if (retriedDownload is not null)
            {
                Retry(retriedDownload, args);
                return;
            }

            // A path the user named in a Save As dialog is theirs, so the file goes where they said and the
            // download list, which records what this session downloaded into the project, records nothing.
            // The download then runs with no UI of its own, since Handled has already suppressed WebView2's.
            if (IsUserChosenPath(resultFilePath))
            {
                _logger.LogDebug("Download saved to the path its Save As dialog named");
                return;
            }

            // Announced before the outcome is known, because the navigation this replaced is cancelled
            // whether or not the download goes on to start.
            DownloadStarted?.Invoke(this, EventArgs.Empty);

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

            var newDownload = new TrackedDownload(ticket.Id, ticket.StagingPath, transfer);
            _trackedDownloads.Add(newDownload);

            Observe(newDownload, downloadOperation);
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

    /// <summary>
    /// Whether the download is going to a path the user named in a Save As dialog. WebView2 writes every
    /// other download to its default download folder, which is the project's downloads folder, so a
    /// destination anywhere else is one the user chose. A path that cannot be compared is treated as an
    /// ordinary download, which keeps the file in the project rather than writing it somewhere nothing
    /// vouched for.
    /// </summary>
    internal static bool IsUserChosenPath(string resultFilePath, string downloadsFolderPath)
    {
        if (string.IsNullOrEmpty(downloadsFolderPath))
        {
            return false;
        }

        try
        {
            var resultFolderPath = Path.GetDirectoryName(Path.GetFullPath(resultFilePath));
            if (string.IsNullOrEmpty(resultFolderPath))
            {
                return false;
            }

            return !string.Equals(
                resultFolderPath.TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(downloadsFolderPath).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Compared against where the service would put the download rather than against the web view's own
    // setting, so a project whose downloads folder has changed since this web view was attached routes the
    // download rather than writing it to the folder the setting still names.
    private bool IsUserChosenPath(string resultFilePath)
    {
        var folderResult = _downloadService.GetDestinationFolderPath();
        if (folderResult.IsFailure)
        {
            return false;
        }

        return IsUserChosenPath(resultFilePath, folderResult.Value);
    }

    /// <summary>
    /// Whether an attempt at a download has been given up on for a retry. The attempt raises no further events,
    /// and goes on reporting that it is in progress along with the reason its transfer broke off, which an
    /// attempt still running does not have.
    /// </summary>
    internal static bool IsAbandoned(CoreWebView2DownloadState state, CoreWebView2DownloadInterruptReason interruptReason)
    {
        return state == CoreWebView2DownloadState.InProgress &&
            interruptReason != CoreWebView2DownloadInterruptReason.None;
    }

    // The download a new attempt retries: one from the same address whose attempt was given up on. A second
    // download of the same address while the first still runs is a download of its own.
    private TrackedDownload? FindRetriedDownload(CoreWebView2DownloadOperation downloadOperation)
    {
        foreach (var trackedDownload in _trackedDownloads)
        {
            var trackedOperation = trackedDownload.Transfer.Operation;
            if (IsAbandoned(trackedOperation.State, trackedOperation.InterruptReason) &&
                string.Equals(trackedOperation.Uri, downloadOperation.Uri, StringComparison.Ordinal))
            {
                return trackedDownload;
            }
        }

        return null;
    }

    // A retry carries on the download it belongs to: the same row and the same staging path, which Chromium
    // has emptied before starting again. It replaces no navigation, so nothing is announced.
    private void Retry(TrackedDownload trackedDownload, CoreWebView2DownloadStartingEventArgs args)
    {
        _logger.LogDebug($"Download {trackedDownload.DownloadId} retried by the platform");

        args.ResultFilePath = trackedDownload.StagingPath;

        StopObserving(trackedDownload);

        var downloadOperation = args.DownloadOperation;
        trackedDownload.Transfer.Operation = downloadOperation;

        Observe(trackedDownload, downloadOperation);
    }

    // Relays an attempt's progress and outcome for as long as it is the attempt under way.
    private void Observe(TrackedDownload trackedDownload, CoreWebView2DownloadOperation downloadOperation)
    {
        var downloadId = trackedDownload.DownloadId;

        _downloadService.ReportProgress(
            downloadId,
            downloadOperation.BytesReceived,
            ReadTotalBytes(downloadOperation));

        void OnBytesReceivedChanged(CoreWebView2DownloadOperation operation, object args)
        {
            _downloadService.ReportProgress(downloadId, operation.BytesReceived, ReadTotalBytes(operation));
        }

        async void OnStateChanged(CoreWebView2DownloadOperation operation, object args)
        {
            // Async-void event handler: an escaping exception ends up on the synchronization
            // context's unhandled-exception channel, so a WebView-side failure is contained here.
            try
            {
                if (operation.State == CoreWebView2DownloadState.Completed)
                {
                    StopObserving(trackedDownload);
                    _trackedDownloads.Remove(trackedDownload);

                    await _downloadService.CompleteAsync(downloadId);
                }
                else if (operation.State == CoreWebView2DownloadState.Interrupted)
                {
                    StopObserving(trackedDownload);
                    _trackedDownloads.Remove(trackedDownload);

                    await SettleInterruptionAsync(downloadId, operation.InterruptReason);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Download state change handler failed");
            }
        }

        downloadOperation.BytesReceivedChanged += OnBytesReceivedChanged;
        downloadOperation.StateChanged += OnStateChanged;

        trackedDownload.StopObserving = () =>
        {
            downloadOperation.BytesReceivedChanged -= OnBytesReceivedChanged;
            downloadOperation.StateChanged -= OnStateChanged;
        };
    }

    // An attempt nothing is relaying raises no further events here, so the handler it was subscribed with
    // stops rooting this surface once WebView2 lets the operation go.
    private static void StopObserving(TrackedDownload trackedDownload)
    {
        trackedDownload.StopObserving?.Invoke();
        trackedDownload.StopObserving = null;
    }

    private static long? ReadTotalBytes(CoreWebView2DownloadOperation downloadOperation)
    {
        var totalBytes = downloadOperation.TotalBytesToReceive;

        return totalBytes > 0 ? totalBytes : null;
    }

    // A transfer stopped by the user is a cancellation, not a failure. One stopped from the download list
    // has already been recorded as canceled, and is left as it is. Either way the operation has stopped, so
    // it is not asked to stop again from inside its own event.
    private async Task SettleInterruptionAsync(long downloadId, CoreWebView2DownloadInterruptReason interruptReason)
    {
        if (interruptReason == CoreWebView2DownloadInterruptReason.UserCanceled)
        {
            await _downloadService.ReportCanceledAsync(downloadId);
            return;
        }

        var reason = _localizerService.GetString("Downloads_TransferFailed");
        await _downloadService.FailAsync(downloadId, reason);
    }
}
