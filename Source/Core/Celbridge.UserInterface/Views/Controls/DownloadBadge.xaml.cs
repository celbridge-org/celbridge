using System.Globalization;
using Celbridge.Downloads;
using Celbridge.UserInterface.Services;
using Celbridge.UserInterface.ViewModels.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Animation;

// The Uno SDK's implicit global usings include System.Windows.Input, which on the Windows head also
// contains a FocusManager type, so the bare name is ambiguous there.
using FocusManager = Microsoft.UI.Xaml.Input.FocusManager;

namespace Celbridge.UserInterface.Views.Controls;

/// <summary>
/// The title bar badge for the downloads this session made. It shows a ring while a transfer runs and a
/// count once they have settled, flashes when one lands or fails, and opens the list of them on click.
/// </summary>
public sealed partial class DownloadBadge : UserControl
{
    // The tallest the list grows before it scrolls, and how far it stops short of the bottom of a window
    // too short to hold that much.
    private const double ListHeightLimit = 560;
    private const double ListWindowClearance = 120;

    // Where the keyboard was in the list before the rows were rebuilt: the download its row showed, if it
    // was on a row, and that row's position.
    private record ListFocus(long? DownloadId, int RowIndex);

    private readonly IStringLocalizer _stringLocalizer;

    private bool _isFlyoutOpen;
    private bool _isFlashRequested;
    private long _flashEndTick;
    private Storyboard? _flashStoryboard;

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

        _flashStoryboard?.Stop();
        _flashStoryboard = null;

        ViewModel.DownloadsChanged -= OnDownloadsChanged;
        ViewModel.DownloadArrived -= OnDownloadArrived;
        DownloadFlyout.Opening -= OnDownloadFlyout_Opening;
        DownloadFlyout.Closed -= OnDownloadFlyout_Closed;
        SizeChanged -= OnDownloadBadge_SizeChanged;

        Loaded -= OnDownloadBadge_Loaded;
        Unloaded -= OnDownloadBadge_Unloaded;
    }

    private void OnDownloadsChanged(object? sender, EventArgs e)
    {
        UpdateBadge();

        if (_isFlyoutOpen)
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
            if (_isFlyoutOpen)
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
        // A run of downloads settling together shares one flash.
        if (_isFlashRequested ||
            Environment.TickCount64 < _flashEndTick)
        {
            return;
        }

        _isFlashRequested = true;

        // Deferred past the next layout pass, so a badge that has only just appeared is on screen when it
        // pulses.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _isFlashRequested = false;

            if (Visibility != Visibility.Visible)
            {
                return;
            }

            _flashStoryboard = AttentionFlash.Play(AttentionOverlay);
            _flashEndTick = Environment.TickCount64 + (long)AttentionFlash.Duration.TotalMilliseconds;
        });
    }

    private void OnDownloadFlyout_Opening(object? sender, object e)
    {
        _isFlyoutOpen = true;

        // A full list would run off the bottom of the window, so it is capped to the window and scrolls.
        var windowHeight = XamlRoot?.Size.Height ?? 0;
        if (windowHeight > 0)
        {
            var listHeight = Math.Min(ListHeightLimit, windowHeight - ListWindowClearance);
            DownloadScrollViewer.MaxHeight = Math.Max(listHeight, 0);
        }

        UpdateList();
    }

    private void OnDownloadFlyout_Closed(object? sender, object e)
    {
        _isFlyoutOpen = false;

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

        var listFocus = FindListFocus();

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

        // A row cannot take focus until it has been laid out, which happens after this returns.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => RestoreListFocus(listFocus));
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
            ? FindListFocus()
            : null;

        // Settling only ever makes Clear All available, so it is enabled before the rows change, ready to
        // take the keyboard from a cancel button that is about to go.
        ClearAllButton.IsEnabled = ViewModel.CanClearAll;

        for (var index = 0; index < rows.Count; index++)
        {
            rows[index].Update(downloads[index]);
        }

        if (listFocus?.DownloadId is long focusedId &&
            settlingIds.Contains(focusedId))
        {
            RestoreListFocus(listFocus);
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

    // A rebuild removes the focused element, so where the keyboard was is noted first and handed back
    // afterwards. Clear All counts as the first row.
    private ListFocus? FindListFocus()
    {
        var xamlRoot = XamlRoot;
        if (xamlRoot is null)
        {
            return null;
        }

        var element = FocusManager.GetFocusedElement(xamlRoot) as DependencyObject;
        while (element is not null)
        {
            if (ReferenceEquals(element, ClearAllButton))
            {
                return new ListFocus(DownloadId: null, RowIndex: 0);
            }

            if (element is DownloadRow row)
            {
                var rowIndex = DownloadRows.Children.IndexOf(row);

                return new ListFocus(row.Download.Id, rowIndex);
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    private void RestoreListFocus(ListFocus listFocus)
    {
        if (!_isFlyoutOpen)
        {
            return;
        }

        var rows = DownloadRows.Children
            .OfType<DownloadRow>()
            .ToList();

        // The row still listing the same download keeps the keyboard, wherever an arrival has moved it.
        var sameRow = rows.FirstOrDefault(row => row.Download.Id == listFocus.DownloadId);
        if (sameRow is not null &&
            sameRow.TryFocusButton())
        {
            return;
        }

        var startIndex = Math.Clamp(listFocus.RowIndex, 0, Math.Max(rows.Count - 1, 0));

        // Forward from the row that took the removed one's place, then back towards the top.
        for (var index = startIndex; index < rows.Count; index++)
        {
            if (rows[index].TryFocusButton())
            {
                return;
            }
        }

        for (var index = startIndex - 1; index >= 0; index--)
        {
            if (rows[index].TryFocusButton())
            {
                return;
            }
        }

        ClearAllButton.Focus(FocusState.Programmatic);
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
