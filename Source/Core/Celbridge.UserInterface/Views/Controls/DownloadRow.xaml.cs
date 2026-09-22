using System.Globalization;
using Celbridge.Downloads;

namespace Celbridge.UserInterface.Views.Controls;

/// <summary>
/// One download in the download list: how far it has got, its file name and how large it is. A running
/// download can be stopped, and clicking a finished one finds its file in the Explorer.
/// </summary>
public sealed partial class DownloadRow : UserControl
{
    private const double BytesPerKilobyte = 1024;
    private const double BytesPerMegabyte = BytesPerKilobyte * 1024;
    private const double BytesPerGigabyte = BytesPerMegabyte * 1024;

    // A rate measured over the first moments of a transfer is mostly the connection opening, so no time
    // remaining is offered until enough of one has passed to divide by.
    private static readonly TimeSpan MinimumElapsedForEstimate = TimeSpan.FromSeconds(2);

    private readonly IStringLocalizer _stringLocalizer;

    /// <summary>
    /// The download this row shows.
    /// </summary>
    public DownloadEntry Download { get; private set; }

    /// <summary>
    /// Raised when the user asks for the row's file to be found in the Explorer.
    /// </summary>
    public event Action<DownloadEntry>? RevealRequested;

    /// <summary>
    /// Raised when the user stops a running download.
    /// </summary>
    public event Action<DownloadEntry>? CancelRequested;

    public DownloadRow(DownloadEntry download, bool isFirstRow)
    {
        InitializeComponent();

        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        Download = download;

        Divider.Visibility = isFirstRow ? Visibility.Collapsed : Visibility.Visible;

        ApplyLabels(download);
        Apply(download);
    }

    /// <summary>
    /// Shows a later state of the same download. Updating the row in place rather than building another
    /// keeps the keyboard where it was while a transfer reports its progress.
    /// </summary>
    public void Update(DownloadEntry download)
    {
        Download = download;

        Apply(download);
    }

    /// <summary>
    /// Moves keyboard focus to what the row offers: its cancel button while it runs, or the row itself
    /// once it has a file to find. Returns false for a failed download, which offers neither.
    /// </summary>
    public bool TryFocusButton()
    {
        if (CancelButton.Visibility == Visibility.Visible)
        {
            return CancelButton.Focus(FocusState.Programmatic);
        }

        if (RevealButton.IsTabStop)
        {
            return RevealButton.Focus(FocusState.Programmatic);
        }

        return false;
    }

    private void Apply(DownloadEntry download)
    {
        ApplyStatus(download);
        ApplyText(download);
        ApplyActions(download);
    }

    private void ApplyStatus(DownloadEntry download)
    {
        var isTransferring = download.Status == DownloadStatus.InProgress;

        SucceededIcon.Visibility = download.Status == DownloadStatus.Succeeded
            ? Visibility.Visible
            : Visibility.Collapsed;

        FailedIcon.Visibility = download.Status == DownloadStatus.Failed
            ? Visibility.Visible
            : Visibility.Collapsed;

        ApplyProgress(download, isTransferring);
    }

    private void ApplyProgress(DownloadEntry download, bool isTransferring)
    {
        if (!isTransferring)
        {
            TransferProgress.Visibility = Visibility.Collapsed;
            TransferProgress.IsIndeterminate = false;
            return;
        }

        TransferProgress.Visibility = Visibility.Visible;

        var totalBytes = download.TotalBytes;
        if (totalBytes is null ||
            totalBytes.Value <= 0)
        {
            TransferProgress.IsIndeterminate = true;
            return;
        }

        TransferProgress.IsIndeterminate = false;
        TransferProgress.Maximum = totalBytes.Value;
        TransferProgress.Value = Math.Min(download.BytesReceived, totalBytes.Value);
    }

    private void ApplyText(DownloadEntry download)
    {
        FileNameText.Text = download.FileName;
        DetailText.Text = ComposeDetail(download);

        if (string.IsNullOrEmpty(download.FailureReason))
        {
            FailureText.Visibility = Visibility.Collapsed;
            return;
        }

        FailureText.Text = download.FailureReason;
        FailureText.Visibility = Visibility.Visible;
    }

    // A running transfer says how far it has got and how long is left, and a settled one says when it
    // arrived, with how large the file turned out to be when there is a file. A failed download left none,
    // so the bytes it received before it stopped are not a size.
    private string ComposeDetail(DownloadEntry download)
    {
        if (download.Status != DownloadStatus.InProgress)
        {
            var parts = new List<string>();

            if (download.Status == DownloadStatus.Succeeded &&
                download.BytesReceived > 0)
            {
                parts.Add(FormatBytes(download.BytesReceived));
            }

            parts.Add(FormatStart(download.StartedAt));

            return string.Join("  ", parts);
        }

        var totalBytes = download.TotalBytes;
        if (totalBytes is null ||
            totalBytes.Value <= 0)
        {
            return FormatBytes(download.BytesReceived);
        }

        var progress = _stringLocalizer.GetString(
            "Downloads_Progress",
            FormatBytes(download.BytesReceived),
            FormatBytes(totalBytes.Value));

        var remaining = FormatRemaining(download, totalBytes.Value);
        if (string.IsNullOrEmpty(remaining))
        {
            return progress;
        }

        return $"{progress}  {remaining}";
    }

    private string FormatRemaining(DownloadEntry download, long totalBytes)
    {
        var elapsed = DateTimeOffset.UtcNow - download.StartedAt;
        if (elapsed < MinimumElapsedForEstimate ||
            download.BytesReceived <= 0)
        {
            return string.Empty;
        }

        var bytesPerSecond = download.BytesReceived / elapsed.TotalSeconds;
        if (bytesPerSecond <= 0)
        {
            return string.Empty;
        }

        var bytesRemaining = totalBytes - download.BytesReceived;
        if (bytesRemaining <= 0)
        {
            return string.Empty;
        }

        var secondsRemaining = bytesRemaining / bytesPerSecond;

        // The compact forms carry no plural, so they are looked up directly rather than through the
        // one-or-many helper the relative times use.
        if (secondsRemaining < 60)
        {
            return _stringLocalizer.GetString("Downloads_TimeLeft_Seconds", (int)Math.Ceiling(secondsRemaining));
        }

        if (secondsRemaining < 3600)
        {
            return _stringLocalizer.GetString("Downloads_TimeLeft_Minutes", (int)Math.Ceiling(secondsRemaining / 60));
        }

        return _stringLocalizer.GetString("Downloads_TimeLeft_Hours", (int)Math.Ceiling(secondsRemaining / 3600));
    }

    // Set once rather than with each update. A running transfer updates the row several times a second,
    // and replacing a tooltip restarts the delay before it shows, so one set on every update never
    // appears. None of this text depends on how far the download has got.
    private void ApplyLabels(DownloadEntry download)
    {
        var cancelText = _stringLocalizer.GetString("Downloads_Cancel");
        ToolTipService.SetToolTip(CancelButton, cancelText);
        AutomationProperties.SetName(CancelButton, $"{cancelText}. {download.FileName}");

        var revealText = _stringLocalizer.GetString("Downloads_Reveal");
        ToolTipService.SetToolTip(RevealButton, revealText);
        AutomationProperties.SetName(RevealButton, $"{download.FileName}. {revealText}");
    }

    // Stopping a transfer and finding the file it produced are never both on offer: a download that is
    // still running has no file to find, and one that has finished cannot be stopped. A row that cannot
    // be clicked raises no pointer events, so its tooltip stays hidden until it has a file to find.
    private void ApplyActions(DownloadEntry download)
    {
        var isTransferring = download.Status == DownloadStatus.InProgress;
        var hasFile = download.Status == DownloadStatus.Succeeded;

        CancelButton.Visibility = isTransferring ? Visibility.Visible : Visibility.Collapsed;

        RevealButton.IsHitTestVisible = hasFile;
        RevealButton.IsTabStop = hasFile;
    }

    private string FormatBytes(long bytes)
    {
        if (bytes >= BytesPerGigabyte)
        {
            return GetSizeString("Downloads_Size_Gigabytes", bytes / BytesPerGigabyte, "0.0");
        }

        if (bytes >= BytesPerMegabyte)
        {
            return GetSizeString("Downloads_Size_Megabytes", bytes / BytesPerMegabyte, "0.0");
        }

        if (bytes >= BytesPerKilobyte)
        {
            return GetSizeString("Downloads_Size_Kilobytes", bytes / BytesPerKilobyte, "0");
        }

        return GetSizeString("Downloads_Size_Bytes", bytes, "0");
    }

    private string GetSizeString(string key, double value, string format)
    {
        var text = value.ToString(format, CultureInfo.CurrentCulture);

        return _stringLocalizer.GetString(key, text);
    }

    // Relative for anything from the last day, which is every download a session holds in practice. The
    // rows are rebuilt each time the list opens, so what they say is current when it is read.
    private string FormatStart(DateTimeOffset startedAt)
    {
        var elapsed = DateTimeOffset.UtcNow - startedAt;

        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return _stringLocalizer.GetString("Downloads_Time_JustNow");
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            var minutes = (int)elapsed.TotalMinutes;

            return GetCountString("Downloads_Time_MinutesAgo", minutes);
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            var hours = (int)elapsed.TotalHours;

            return GetCountString("Downloads_Time_HoursAgo", hours);
        }

        return startedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    }

    private string GetCountString(string baseKey, int count)
    {
        var key = count == 1
            ? $"{baseKey}_One"
            : $"{baseKey}_Many";

        return _stringLocalizer.GetString(key, count);
    }

    private void RevealButton_Click(object sender, RoutedEventArgs e)
    {
        RevealRequested?.Invoke(Download);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke(Download);
    }
}
