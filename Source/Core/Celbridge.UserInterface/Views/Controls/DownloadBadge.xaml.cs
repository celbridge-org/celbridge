using System.Globalization;
using Celbridge.Downloads;
using Celbridge.UserInterface.Services;
using Celbridge.UserInterface.ViewModels.Controls;

namespace Celbridge.UserInterface.Views.Controls;

/// <summary>
/// The title bar badge for the downloads this session made. It shows a ring while a transfer runs and a
/// count once they have settled, flashes when one lands or fails, and opens the list of them on click.
/// </summary>
public sealed partial class DownloadBadge : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly BadgeList _list;

    public DownloadBadgeViewModel ViewModel { get; }

    /// <summary>
    /// Raised when the badge appears, disappears or changes width.
    /// </summary>
    public event EventHandler? LayoutChanged;

    public DownloadBadge()
    {
        InitializeComponent();

        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        ViewModel = ServiceLocator.AcquireService<DownloadBadgeViewModel>();

        var overlayFlyoutSupport = ServiceLocator.AcquireService<IOverlayFlyoutSupport>();
        overlayFlyoutSupport.Apply(DownloadFlyout);

        _list = new BadgeList(this, DownloadScrollViewer, DownloadRows, ClearAllButton, AttentionOverlay);

        // Never unsubscribed, so a badge the title bar removes and re-adds starts listening again instead
        // of going quiet for the rest of the session.
        Loaded += OnDownloadBadge_Loaded;
        Unloaded += OnDownloadBadge_Unloaded;
    }

    private void OnDownloadBadge_Loaded(object sender, RoutedEventArgs e)
    {
        TitleText.Text = _stringLocalizer.GetString("Downloads_Title");
        ClearAllButton.Content = _stringLocalizer.GetString("Downloads_ClearAll");
        ToolTipService.SetToolTip(ClearAllButton, _stringLocalizer.GetString("Downloads_ClearAllTooltip"));
        ToolTipService.SetPlacement(BadgeButton, PlacementMode.Bottom);

        ViewModel.DownloadsChanged += OnDownloadsChanged;
        ViewModel.DownloadArrived += OnDownloadArrived;
        DownloadFlyout.Opening += OnDownloadFlyout_Opening;
        DownloadFlyout.Closed += OnDownloadFlyout_Closed;
        SizeChanged += OnDownloadBadge_SizeChanged;

        ViewModel.OnLoaded();

        UpdateBadge();
    }

    private void OnDownloadBadge_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnUnloaded();

        _list.StopFlash();

        ViewModel.DownloadsChanged -= OnDownloadsChanged;
        ViewModel.DownloadArrived -= OnDownloadArrived;
        DownloadFlyout.Opening -= OnDownloadFlyout_Opening;
        DownloadFlyout.Closed -= OnDownloadFlyout_Closed;
        SizeChanged -= OnDownloadBadge_SizeChanged;
    }

    private void OnDownloadsChanged(object? sender, EventArgs e)
    {
        UpdateBadge();

        if (_list.IsOpen)
        {
            UpdateList();
        }
    }

    private void UpdateBadge()
    {
        var downloads = ViewModel.Downloads;
        if (downloads.Count == 0)
        {
            // The badge anchors the list, so an open list closes before the badge collapses from under it.
            // The collapse follows once the list has closed.
            if (_list.IsOpen)
            {
                DownloadFlyout.Hide();
                return;
            }

            SetBadgeVisible(false);
            return;
        }

        ApplyGlyph(ViewModel.IsTransferring, ViewModel.HasFailure);

        // A list holding only canceled downloads still has a badge to open it by, but nothing to count.
        var badgeCount = ViewModel.BadgeCount;
        CountText.Text = badgeCount.ToString(CultureInfo.CurrentCulture);
        CountText.Visibility = badgeCount > 0 ? Visibility.Visible : Visibility.Collapsed;

        var summary = ViewModel.Summary;
        ToolTipService.SetToolTip(BadgeButton, summary);
        AutomationProperties.SetName(BadgeButton, summary);

        SetBadgeVisible(true);
    }

    // A running transfer takes the ring, since it is the one state that is still changing. What settled
    // shows as a count beside it either way.
    private void ApplyGlyph(bool isTransferring, bool hasFailure)
    {
        TransferRing.IsActive = isTransferring;
        TransferRing.Visibility = isTransferring ? Visibility.Visible : Visibility.Collapsed;

        var isSettled = !isTransferring;

        SettledIcon.Visibility = isSettled && !hasFailure ? Visibility.Visible : Visibility.Collapsed;
        FailedIcon.Visibility = isSettled && hasFailure ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetBadgeVisible(bool isVisible)
    {
        var visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        if (Visibility == visibility)
        {
            return;
        }

        Visibility = visibility;

        // A control that collapses raises no SizeChanged, so its disappearance has to be announced here.
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnDownloadBadge_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnDownloadArrived(object? sender, EventArgs e)
    {
        _list.Flash();
    }

    private void OnDownloadFlyout_Opening(object? sender, object e)
    {
        _list.IsOpen = true;
        _list.CapHeightToWindow();

        UpdateList();
    }

    private void OnDownloadFlyout_Closed(object? sender, object e)
    {
        _list.IsOpen = false;

        // The next open builds the rows again from whatever is recorded by then, which is also what keeps
        // each row's relative time current.
        ClearRows();

        // The list may have emptied while it was open, in which case the badge was left in place to anchor
        // it and collapses now.
        UpdateBadge();
    }

    private void UpdateList()
    {
        var downloads = ViewModel.Downloads;

        // A running transfer reports its progress several times a second, and every one of those is a
        // change. Rebuilding the rows each time would take the keyboard away from whoever was using them
        // and flicker the list, so the same rows are told the new state instead.
        if (TryUpdateRowsInPlace(downloads))
        {
            return;
        }

        var listFocus = _list.FindFocus();

        ClearRows();

        for (var index = 0; index < downloads.Count; index++)
        {
            var row = new DownloadRow(downloads[index], isFirstRow: index == 0);
            row.RevealRequested += OnRowRevealRequested;
            row.CancelRequested += OnRowCancelRequested;
            row.RemoveRequested += OnRowRemoveRequested;

            DownloadRows.Children.Add(row);
        }

        // A list whose downloads are all still running has nothing to clear. The button is disabled only
        // after the keyboard's place has been noted, because a disabled button drops the focus.
        ClearAllButton.IsEnabled = ViewModel.CanClearAll;

        if (listFocus is null)
        {
            return;
        }

        _list.RestoreFocusWhenLaidOut(listFocus);
    }

    // True when the list holds the same downloads in the same order, so every row still has one to show.
    private bool TryUpdateRowsInPlace(IReadOnlyList<DownloadEntry> downloads)
    {
        if (DownloadRows.Children.Count != downloads.Count)
        {
            return false;
        }

        var rows = DownloadRows.Children
            .OfType<DownloadRow>()
            .ToList();

        if (rows.Count != downloads.Count)
        {
            return false;
        }

        for (var index = 0; index < rows.Count; index++)
        {
            if (rows[index].Download.Id != downloads[index].Id)
            {
                return false;
            }
        }

        // A download that settles hides its row's cancel button, so the keyboard on that button is noted
        // before it goes. Focus left on a hidden button falls out of the list, which on macOS takes it to
        // the placeholder and closes the list.
        var settlingIds = new HashSet<long>();
        for (var index = 0; index < rows.Count; index++)
        {
            if (rows[index].Download.Status == DownloadStatus.InProgress &&
                downloads[index].Status != DownloadStatus.InProgress)
            {
                settlingIds.Add(downloads[index].Id);
            }
        }

        var listFocus = settlingIds.Count > 0
            ? _list.FindFocus()
            : null;

        // Settling only ever makes Clear All available, so it is enabled before the rows change, ready to
        // take the keyboard from a cancel button that is about to go.
        ClearAllButton.IsEnabled = ViewModel.CanClearAll;

        for (var index = 0; index < rows.Count; index++)
        {
            rows[index].Update(downloads[index]);
        }

        if (listFocus?.EntryId is long focusedId &&
            settlingIds.Contains(focusedId))
        {
            _list.RestoreFocus(listFocus);
        }

        return true;
    }

    private void ClearRows()
    {
        foreach (var child in DownloadRows.Children)
        {
            if (child is DownloadRow row)
            {
                row.RevealRequested -= OnRowRevealRequested;
                row.CancelRequested -= OnRowCancelRequested;
                row.RemoveRequested -= OnRowRemoveRequested;
            }
        }

        DownloadRows.Children.Clear();
    }

    private void OnRowRevealRequested(DownloadEntry download)
    {
        DownloadFlyout.Hide();

        ViewModel.Reveal(download);
    }

    private void OnRowCancelRequested(DownloadEntry download)
    {
        ViewModel.Cancel(download);
    }

    // The list is rebuilt without the row, which hands the keyboard to the row that took its place.
    private void OnRowRemoveRequested(DownloadEntry download)
    {
        ViewModel.Remove(download);
    }

    private void ClearAllButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearAll();
    }
}
