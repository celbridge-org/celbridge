using System.Globalization;
using Celbridge.Downloads;

namespace Celbridge.UserInterface.Views.Controls;

/// <summary>
/// One download in the download list: how far it has got, its file name and how large it is. A running
/// download can be stopped, and a finished one can be taken off the list. Clicking a completed one finds its
/// file in the Explorer.
/// </summary>
public sealed partial class DownloadRow : UserControl, IBadgeRow
{
    private const double BytesPerKilobyte = 1024;
    private const double BytesPerMegabyte = BytesPerKilobyte * 1024;
    private const double BytesPerGigabyte = BytesPerMegabyte * 1024;

    // A rate measured over the first moments of a transfer is mostly the connection opening, so no time
    // remaining is shown until this much has elapsed.
    private static readonly TimeSpan MinimumElapsedForEstimate = TimeSpan.FromSeconds(2);

    private readonly IStringLocalizer _stringLocalizer;

    /// <summary>
    /// The download this row shows.
    /// </summary>
    public DownloadEntry Download { get; private set; }

    long IBadgeRow.EntryId => Download.Id;

    /// <summary>
    /// Raised when the user asks for the row's file to be found in the Explorer.
    /// </summary>
    public event Action<DownloadEntry>? RevealRequested;

    /// <summary>
    /// Raised when the user stops a running download.
    /// </summary>
    public event Action<DownloadEntry>? CancelRequested;

    /// <summary>
    /// Raised when the user takes a finished download off the list.
    /// </summary>
    public event Action<DownloadEntry>? RemoveRequested;

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
    /// Moves keyboard focus to what the row offers first: its cancel button while it runs, the row itself
    /// once it has a file to find, or otherwise its remove button.
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

        if (RemoveButton.Visibility == Visibility.Visible)
        {
            return RemoveButton.Focus(FocusState.Programmatic);
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

        CanceledIcon.Visibility = download.Status == DownloadStatus.Canceled
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

        CanceledText.Visibility = download.Status == DownloadStatus.Canceled
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (string.IsNullOrEmpty(download.FailureReason))
        {
            FailureText.Visibility = Visibility.Collapsed;
            return;
        }

        FailureText.Text = download.FailureReason;
        FailureText.Visibility = Visibility.Visible;
    }

    // A running transfer says how far it has got and how long is left. A settled one says when it arrived,
    // and how large the file is when there is one: a failed or canceled download left no file, so the
    // bytes it received are not a size.
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

    // Set once rather than on every update. A running transfer updates the row several times a second, and
    // replacing a tooltip restarts the delay before it shows, so a tooltip reset that often would never
    // appear. None of this text changes as the download progresses.
    private void ApplyLabels(DownloadEntry download)
    {
        var cancelText = _stringLocalizer.GetString("Downloads_Cancel");
        ToolTipService.SetToolTip(CancelButton, cancelText);
        AutomationProperties.SetName(CancelButton, $"{cancelText}. {download.FileName}");

        var revealText = _stringLocalizer.GetString("Downloads_Reveal");
        ToolTipService.SetToolTip(RevealButton, revealText);
        AutomationProperties.SetName(RevealButton, $"{download.FileName}. {revealText}");

        var removeText = _stringLocalizer.GetString("Downloads_Remove");
        ToolTipService.SetToolTip(RemoveButton, removeText);
        AutomationProperties.SetName(RemoveButton, $"{removeText}. {download.FileName}");

        CanceledText.Text = _stringLocalizer.GetString("Downloads_TransferCancelled");
    }

    // Stopping a transfer and finding the file it produced are never both on offer: a download still
    // running has no file to find, and a finished one cannot be stopped. Any finished download can be
    // taken off the list. A row that cannot be clicked raises no pointer events, so its tooltip stays
    // hidden until there is a file to find.
    private void ApplyActions(DownloadEntry download)
    {
        var isTransferring = download.Status == DownloadStatus.InProgress;
        var hasFile = download.Status == DownloadStatus.Succeeded;

        CancelButton.Visibility = isTransferring ? Visibility.Visible : Visibility.Collapsed;
        RemoveButton.Visibility = isTransferring ? Visibility.Collapsed : Visibility.Visible;

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
    // rows are rebuilt each time the list opens, so what they say is current whenever it is read.
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

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        RemoveRequested?.Invoke(Download);
    }
}
