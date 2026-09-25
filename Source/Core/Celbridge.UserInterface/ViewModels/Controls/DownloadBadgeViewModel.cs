using Celbridge.Commands;
using Celbridge.Downloads;
using Celbridge.Explorer;
using Celbridge.Localization;
using Celbridge.Utilities;

namespace Celbridge.UserInterface.ViewModels.Controls;

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
    /// Whether a transfer is still running, which is when the badge shows a progress ring.
    /// </summary>
    public bool IsTransferring { get; private set; }

    /// <summary>
    /// Whether any recorded download failed, which turns the badge to the error colour.
    /// </summary>
    public bool HasFailure { get; private set; }

    /// <summary>
    /// The number the badge shows: every recorded download except the canceled ones, which stay in the
    /// list but are not counted.
    /// </summary>
    public int BadgeCount { get; private set; }

    /// <summary>
    /// Whether any recorded download has finished, which is what Clear All takes off the list.
    /// </summary>
    public bool CanClearAll { get; private set; }

    /// <summary>
    /// What the badge's tooltip and accessible name say about the recorded downloads.
    /// </summary>
    public string Summary { get; private set; } = string.Empty;

    public DownloadBadgeViewModel(
        IMessengerService messengerService,
        IDispatcher dispatcher,
        ILocalizerService localizerService,
        IDownloadService downloadService,
        ICommandService commandService)
    {
        _messengerService = messengerService;
        _dispatcher = dispatcher;
        _localizerService = localizerService;
        _downloadService = downloadService;
        _commandService = commandService;
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
    /// Finds a completed download in the Explorer, leaving its record in the list.
    /// </summary>
    public void Reveal(DownloadEntry download)
    {
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
        IsTransferring = downloads.Any(download => download.Status == DownloadStatus.InProgress);
        HasFailure = downloads.Any(download => download.Status == DownloadStatus.Failed);
        BadgeCount = downloads.Count(download => download.Status != DownloadStatus.Canceled);
        CanClearAll = downloads.Any(download => download.Status != DownloadStatus.InProgress);
        Summary = ComposeSummary(downloads);
    }

    // A lone download is summarised by its file name, and several by a count of each outcome.
    private string ComposeSummary(IReadOnlyList<DownloadEntry> downloads)
    {
        if (downloads.Count == 0)
        {
            return string.Empty;
        }

        if (downloads.Count == 1)
        {
            return downloads[0].FileName;
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
}
