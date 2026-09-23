using System.Globalization;
using Celbridge.Documents;
using Celbridge.Notifications;
using Celbridge.Reports;
using Celbridge.UserInterface.Services;
using Celbridge.UserInterface.ViewModels.Controls;

namespace Celbridge.UserInterface.Views.Controls;

/// <summary>
/// The title bar badge for the pending notifications. It states the most serious severity and how many are
/// pending, flashes when one arrives, and opens the list of them on click.
/// </summary>
public sealed partial class NotificationBadge : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly BadgeList _list;

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

        _list = new BadgeList(this, NotificationScrollViewer, NotificationRows, ClearAllButton, AttentionOverlay);

        // Never unsubscribed, so a badge the title bar removes and re-adds starts listening again instead
        // of going quiet for the rest of the session.
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

        _list.StopFlash();

        ViewModel.NotificationsChanged -= OnNotificationsChanged;
        ViewModel.NotificationArrived -= OnNotificationArrived;
        NotificationFlyout.Opening -= OnNotificationFlyout_Opening;
        NotificationFlyout.Closed -= OnNotificationFlyout_Closed;
        SizeChanged -= OnNotificationBadge_SizeChanged;
    }

    private void OnNotificationsChanged(object? sender, EventArgs e)
    {
        UpdateBadge();

        if (_list.IsOpen)
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
            if (_list.IsOpen)
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
        _list.Flash();
    }

    private void OnNotificationFlyout_Opening(object? sender, object e)
    {
        _list.IsOpen = true;
        _list.CapHeightToWindow();

        UpdateList();
    }

    private void OnNotificationFlyout_Closed(object? sender, object e)
    {
        _list.IsOpen = false;

        // The next open builds the rows again from whatever is pending by then.
        ClearRows();

        // The list may have emptied while it was open, in which case the badge was left in place to anchor it
        // and collapses now.
        UpdateBadge();
    }

    private void UpdateList()
    {
        var listFocus = _list.FindFocus();

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

        _list.RestoreFocusWhenLaidOut(listFocus);
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
