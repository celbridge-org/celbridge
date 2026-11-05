using Celbridge.Commands;
using Celbridge.Downloads;
using Celbridge.Explorer;
using Celbridge.Localization;
using Celbridge.Logging;
using Celbridge.Platform;
using Celbridge.Utilities;

namespace Celbridge.UserInterface.ViewModels.Controls;

/// <summary>
/// Lists the four states of the download badge. The badge shows one state at a time.
/// </summary>
public enum DownloadBadgeState
{
    /// <summary>
    /// A download runs, and the newest running download has a known size.
    /// </summary>
    InProgress,

    /// <summary>
    /// A download runs, and the server gave no size for the newest running download.
    /// </summary>
    UnknownProgress,

    /// <summary>
    /// Every download has finished, and the newest row succeeded or was canceled.
    /// </summary>
    Settled,

    /// <summary>
    /// Every download has finished, and the newest row is a failure.
    /// </summary>
    Failed
}

/// <summary>
/// Drives the download badge and the list it opens: what this session has downloaded, what the badge says
/// about it, and what the user can do with each row.
/// </summary>
public class DownloadBadgeViewModel
{
    private readonly IMessengerService _messengerService;
    private readonly IDispatcher _dispatcher;
    private readonly ILocalizerService _localizerService;
    private readonly IDownloadService _downloadService;
    private readonly ICommandService _commandService;
    private readonly IFileManagerLauncher _fileManagerLauncher;
    private readonly ILogger<DownloadBadgeViewModel> _logger;

    /// <summary>
    /// Raised on the UI thread when the recorded downloads change.
    /// </summary>
    public event EventHandler? DownloadsChanged;

    /// <summary>
    /// Raised on the UI thread when a download settles, after DownloadsChanged.
    /// </summary>
    public event EventHandler? DownloadArrived;

    /// <summary>
    /// The recorded downloads, newest first.
    /// </summary>
    public IReadOnlyList<DownloadEntry> Downloads { get; private set; } = Array.Empty<DownloadEntry>();

    /// <summary>
    /// Gets the badge's current state. While any download runs, the newest running download decides the state. The
    /// user most likely waits for that download. Once every download has finished, the newest row decides the state.
    /// </summary>
    public DownloadBadgeState State { get; private set; } = DownloadBadgeState.Settled;

    /// <summary>
    /// How much of the newest running download has arrived, from 0 to 1. Zero unless the state is InProgress.
    /// </summary>
    public double Progress { get; private set; }

    /// <summary>
    /// Whether any recorded download has finished, which is what Clear All takes off the list.
    /// </summary>
    public bool CanClearAll { get; private set; }

    /// <summary>
    /// Gets the summary of the recorded downloads for the badge's help text.
    /// </summary>
    public string Summary { get; private set; } = string.Empty;

    public DownloadBadgeViewModel(
        IMessengerService messengerService,
        IDispatcher dispatcher,
        ILocalizerService localizerService,
        IDownloadService downloadService,
        ICommandService commandService,
        IFileManagerLauncher fileManagerLauncher,
        ILogger<DownloadBadgeViewModel> logger)
    {
        _messengerService = messengerService;
        _dispatcher = dispatcher;
        _localizerService = localizerService;
        _downloadService = downloadService;
        _commandService = commandService;
        _fileManagerLauncher = fileManagerLauncher;
        _logger = logger;
    }

    public void OnLoaded()
    {
        _messengerService.Register<DownloadsChangedMessage>(this, OnDownloadsChanged);

        Refresh();
    }

    public void OnUnloaded()
    {
        _messengerService.UnregisterAll(this);
    }

    /// <summary>
    /// Finds a completed download's file, leaving its record in the list. A file in the project shows in the
    /// Explorer, and a file outside the project shows in the system file manager.
    /// </summary>
    public void Reveal(DownloadEntry download)
    {
        if (!string.IsNullOrEmpty(download.FilePath))
        {
            _ = RevealInFileManagerAsync(download.FilePath);
            return;
        }

        if (download.Resource.IsEmpty)
        {
            return;
        }

        _commandService.Execute<ISelectResourceCommand>(command =>
        {
            command.Resource = download.Resource;
            command.ShowExplorerPanel = true;
        });
    }

    /// <summary>
    /// Checks the files saved outside the project. A row leaves the list once the row's file has gone. The service
    /// follows the files in the project as they change, but nothing follows the others.
    /// </summary>
    public void CheckFilesOutsideProject()
    {
        _ = _downloadService.CheckFilesOutsideProjectAsync();
    }

    /// <summary>
    /// Stops a download that is still running, which records it as cancelled.
    /// </summary>
    public void Cancel(DownloadEntry download)
    {
        _ = _downloadService.CancelAsync(download.Id);
    }

    /// <summary>
    /// Takes a finished download off the list, leaving the file it landed on in the project.
    /// </summary>
    public void Remove(DownloadEntry download)
    {
        _downloadService.Remove(download.Id);
    }

    public void ClearAll()
    {
        _downloadService.ClearAll();
    }

    private void OnDownloadsChanged(object recipient, DownloadsChangedMessage message)
    {
        // Sent from whichever thread recorded the change.
        _dispatcher.TryEnqueue(() =>
        {
            Refresh();

            DownloadsChanged?.Invoke(this, EventArgs.Empty);

            if (message.HasArrival)
            {
                DownloadArrived?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    private void Refresh()
    {
        var downloads = _downloadService.Downloads;

        Downloads = downloads;
        var report = ReportState(downloads);
        State = report.State;
        Progress = report.Progress;
        CanClearAll = downloads.Any(download => download.Status != DownloadStatus.InProgress);
        Summary = ComposeSummary(downloads);
    }

    // The downloads are newest first, so the first running one is the newest. Once every download has finished, the
    // newest row decides the state. A later success gives Settled, even with a failed row further down the list.
    private static BadgeReport ReportState(IReadOnlyList<DownloadEntry> downloads)
    {
        var newestRunning = downloads.FirstOrDefault(download => download.Status == DownloadStatus.InProgress);
        if (newestRunning is not null)
        {
            if (newestRunning.TotalBytes is not long totalBytes ||
                totalBytes <= 0)
            {
                return new BadgeReport(DownloadBadgeState.UnknownProgress, 0);
            }

            var progress = Math.Clamp((double)newestRunning.BytesReceived / totalBytes, 0, 1);
            return new BadgeReport(DownloadBadgeState.InProgress, progress);
        }

        if (downloads.Count > 0 &&
            downloads[0].Status == DownloadStatus.Failed)
        {
            return new BadgeReport(DownloadBadgeState.Failed, 0);
        }

        return new BadgeReport(DownloadBadgeState.Settled, 0);
    }

    // Summarises the downloads as a count per outcome, even when there's only one. The list shows the file names.
    private string ComposeSummary(IReadOnlyList<DownloadEntry> downloads)
    {
        if (downloads.Count == 0)
        {
            return string.Empty;
        }

        var inProgressCount = downloads.Count(download => download.Status == DownloadStatus.InProgress);
        var succeededCount = downloads.Count(download => download.Status == DownloadStatus.Succeeded);
        var failedCount = downloads.Count(download => download.Status == DownloadStatus.Failed);
        var canceledCount = downloads.Count(download => download.Status == DownloadStatus.Canceled);

        var sentences = new List<string>();

        AddCountSentence(sentences, inProgressCount, "Downloads_Summary_InProgress");
        AddCountSentence(sentences, failedCount, "Downloads_Summary_Failed");
        AddCountSentence(sentences, succeededCount, "Downloads_Summary_Succeeded");
        AddCountSentence(sentences, canceledCount, "Downloads_Summary_Canceled");

        return string.Join(" ", sentences);
    }

    private void AddCountSentence(List<string> sentences, int count, string baseKey)
    {
        if (count == 0)
        {
            return;
        }

        var key = count == 1
            ? $"{baseKey}_One"
            : $"{baseKey}_Many";

        var sentence = _localizerService.GetString(key, count);
        sentences.Add(sentence);
    }

    private async Task RevealInFileManagerAsync(string filePath)
    {
        var openResult = await _fileManagerLauncher.OpenFileManagerAsync(filePath);
        if (openResult.IsFailure)
        {
            _logger.LogWarning(openResult, $"Failed to show the downloaded file '{filePath}' in the file manager");
        }
    }

    // The badge's state, and how much of the newest running download has arrived.
    private record BadgeReport(DownloadBadgeState State, double Progress);
}
