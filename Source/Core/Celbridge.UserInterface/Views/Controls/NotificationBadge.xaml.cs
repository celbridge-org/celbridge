using System.Globalization;
using Celbridge.Documents;
using Celbridge.Notifications;
using Celbridge.Reports;
using Celbridge.UserInterface.Services;
using Celbridge.UserInterface.ViewModels.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Animation;

// The Uno SDK's implicit global usings include System.Windows.Input, which on the Windows head also
// contains a FocusManager type, so the bare name is ambiguous there.
using FocusManager = Microsoft.UI.Xaml.Input.FocusManager;

namespace Celbridge.UserInterface.Views.Controls;

/// <summary>
/// The title bar badge for the pending notifications. It states the most serious severity and how many are
/// pending, flashes when one arrives, and opens the list of them on click.
/// </summary>
public sealed partial class NotificationBadge : UserControl
{
    // The tallest the list grows before it scrolls, and how far it stops short of the bottom of a window too
    // short to hold that much.
    private const double ListHeightLimit = 560;
    private const double ListWindowClearance = 120;

    // Where the keyboard was in the list before the rows were rebuilt: the notification its row showed, if it
    // was on a row, and that row's position.
    private record ListFocus(long? NotificationId, int RowIndex);

    private readonly IStringLocalizer _stringLocalizer;

    private bool _isFlyoutOpen;
    private bool _isFlashRequested;
    private long _flashEndTick;
    private Storyboard? _flashStoryboard;

    public NotificationBadgeViewModel ViewModel { get; }

    /// <summary>
    /// Raised when the badge appears, disappears or changes width.
    /// </summary>
    public event EventHandler? LayoutChanged;

    public NotificationBadge()
    {
        InitializeComponent();

        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        ViewModel = ServiceLocator.AcquireService<NotificationBadgeViewModel>();

        var overlayFlyoutSupport = ServiceLocator.AcquireService<IOverlayFlyoutSupport>();
        overlayFlyoutSupport.Apply(NotificationFlyout);

        Loaded += OnNotificationBadge_Loaded;
        Unloaded += OnNotificationBadge_Unloaded;
    }

    private void OnNotificationBadge_Loaded(object sender, RoutedEventArgs e)
    {
        TitleText.Text = _stringLocalizer.GetString("NotificationCentre_Title");
        ClearAllButton.Content = _stringLocalizer.GetString("NotificationCentre_ClearAll");
        ToolTipService.SetPlacement(BadgeButton, PlacementMode.Bottom);

        ViewModel.NotificationsChanged += OnNotificationsChanged;
        ViewModel.NotificationArrived += OnNotificationArrived;
        NotificationFlyout.Opening += OnNotificationFlyout_Opening;
        NotificationFlyout.Closed += OnNotificationFlyout_Closed;
        SizeChanged += OnNotificationBadge_SizeChanged;

        ViewModel.OnLoaded();

        UpdateBadge();
    }

    private void OnNotificationBadge_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.OnUnloaded();

        _flashStoryboard?.Stop();
        _flashStoryboard = null;

        ViewModel.NotificationsChanged -= OnNotificationsChanged;
        ViewModel.NotificationArrived -= OnNotificationArrived;
        NotificationFlyout.Opening -= OnNotificationFlyout_Opening;
        NotificationFlyout.Closed -= OnNotificationFlyout_Closed;
        SizeChanged -= OnNotificationBadge_SizeChanged;

        Loaded -= OnNotificationBadge_Loaded;
        Unloaded -= OnNotificationBadge_Unloaded;
    }

    private void OnNotificationsChanged(object? sender, EventArgs e)
    {
        UpdateBadge();

        if (_isFlyoutOpen)
        {
            UpdateList();
        }
    }

    private void UpdateBadge()
    {
        var notifications = ViewModel.Notifications;
        if (notifications.Count == 0)
        {
            // The badge anchors the list, so an open list closes before the badge collapses from under it. The
            // collapse follows once the list has closed.
            if (_isFlyoutOpen)
            {
                NotificationFlyout.Hide();
                return;
            }

            SetBadgeVisible(false);
            return;
        }

        ApplySeverity(ViewModel.Severity);

        CountText.Text = notifications.Count.ToString(CultureInfo.CurrentCulture);

        var summary = ViewModel.Summary;
        ToolTipService.SetToolTip(BadgeButton, summary);
        AutomationProperties.SetName(BadgeButton, summary);

        SetBadgeVisible(true);
    }

    private void ApplySeverity(ReportSeverity severity)
    {
        ErrorIcon.Visibility = severity == ReportSeverity.Error ? Visibility.Visible : Visibility.Collapsed;
        WarningIcon.Visibility = severity == ReportSeverity.Warning ? Visibility.Visible : Visibility.Collapsed;
        InfoIcon.Visibility = severity == ReportSeverity.Info ? Visibility.Visible : Visibility.Collapsed;
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

    private void OnNotificationBadge_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnNotificationArrived(object? sender, EventArgs e)
    {
        // A run of arrivals shares one flash, since an editor can raise several in quick succession.
        if (_isFlashRequested ||
            Environment.TickCount64 < _flashEndTick)
        {
            return;
        }

        _isFlashRequested = true;

        // Deferred past the next layout pass, so a badge that has only just appeared is on screen when it pulses.
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

    private void OnNotificationFlyout_Opening(object? sender, object e)
    {
        _isFlyoutOpen = true;

        // A full list would run off the bottom of the window, so it is capped to the window and scrolls.
        var windowHeight = XamlRoot?.Size.Height ?? 0;
        if (windowHeight > 0)
        {
            var listHeight = Math.Min(ListHeightLimit, windowHeight - ListWindowClearance);
            NotificationScrollViewer.MaxHeight = Math.Max(listHeight, 0);
        }

        UpdateList();
    }

    private void OnNotificationFlyout_Closed(object? sender, object e)
    {
        _isFlyoutOpen = false;

        // The next open builds the rows again from whatever is pending by then.
        ClearRows();

        // The list may have emptied while it was open, in which case the badge was left in place to anchor it
        // and collapses now.
        UpdateBadge();
    }

    private void UpdateList()
    {
        var listFocus = FindListFocus();

        ClearRows();

        var notifications = ViewModel.Notifications;
        for (var index = 0; index < notifications.Count; index++)
        {
            var row = new NotificationRow(notifications[index], isFirstRow: index == 0);
            row.ActionRequested += OnRowActionRequested;
            row.DismissRequested += OnRowDismissRequested;

            NotificationRows.Children.Add(row);
        }

        ClearAllButton.Visibility = ViewModel.HasEvents ? Visibility.Visible : Visibility.Collapsed;

        if (listFocus is null)
        {
            return;
        }

        // A row cannot take focus until it has been laid out, which happens after this returns.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => RestoreListFocus(listFocus));
    }

    private void ClearRows()
    {
        foreach (var child in NotificationRows.Children)
        {
            if (child is NotificationRow row)
            {
                row.ActionRequested -= OnRowActionRequested;
                row.DismissRequested -= OnRowDismissRequested;
            }
        }

        NotificationRows.Children.Clear();
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
                return new ListFocus(NotificationId: null, RowIndex: 0);
            }

            if (element is NotificationRow row)
            {
                var rowIndex = NotificationRows.Children.IndexOf(row);

                return new ListFocus(row.Notification.Id, rowIndex);
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

        var rows = NotificationRows.Children
            .OfType<NotificationRow>()
            .ToList();

        // The row still listing the same notification keeps the keyboard, wherever an arrival has moved it.
        var sameRow = rows.FirstOrDefault(row => row.Notification.Id == listFocus.NotificationId);
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

        if (ClearAllButton.Visibility == Visibility.Visible)
        {
            ClearAllButton.Focus(FocusState.Programmatic);
        }
    }

    private void OnRowActionRequested(OpenDocumentAction action)
    {
        NotificationFlyout.Hide();

        ViewModel.PerformAction(action);
    }

    private void OnRowDismissRequested(NotificationEntry notification)
    {
        ViewModel.Dismiss(notification);
    }

    private void ClearAllButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearEvents();
    }
}
