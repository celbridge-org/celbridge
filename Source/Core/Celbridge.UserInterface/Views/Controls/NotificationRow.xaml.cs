using System.Globalization;
using Celbridge.Documents;
using Celbridge.Notifications;
using Celbridge.Reports;

namespace Celbridge.UserInterface.Views.Controls;

/// <summary>
/// One pending notification in the notification list: its severity glyph, its line, when it arrived, its
/// action, and a dismiss button when it is an event.
/// </summary>
public sealed partial class NotificationRow : UserControl
{
    private readonly IStringLocalizer _stringLocalizer;

    /// <summary>
    /// The notification this row shows.
    /// </summary>
    public NotificationEntry Notification { get; }

    /// <summary>
    /// Raised when the user activates the row's action.
    /// </summary>
    public event Action<OpenDocumentAction>? ActionRequested;

    /// <summary>
    /// Raised when the user dismisses the row's event.
    /// </summary>
    public event Action<NotificationEntry>? DismissRequested;

    public NotificationRow(NotificationEntry notification, bool isFirstRow)
    {
        InitializeComponent();

        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();

        Notification = notification;

        Divider.Visibility = isFirstRow ? Visibility.Collapsed : Visibility.Visible;

        ApplySeverity(notification.Content.Severity);
        ApplyText(notification);
        ApplyAction(notification.Content.Action);
        ApplyDismiss(notification.Kind);
    }

    /// <summary>
    /// Moves keyboard focus to the row's dismiss button, or to its action when it cannot be dismissed. Returns
    /// false when the row carries neither.
    /// </summary>
    public bool TryFocusButton()
    {
        if (DismissButton.Visibility == Visibility.Visible)
        {
            return DismissButton.Focus(FocusState.Programmatic);
        }

        if (ActionButton.Visibility == Visibility.Visible)
        {
            return ActionButton.Focus(FocusState.Programmatic);
        }

        return false;
    }

    private void ApplySeverity(ReportSeverity severity)
    {
        ErrorIcon.Visibility = severity == ReportSeverity.Error ? Visibility.Visible : Visibility.Collapsed;
        WarningIcon.Visibility = severity == ReportSeverity.Warning ? Visibility.Visible : Visibility.Collapsed;
        InfoIcon.Visibility = severity == ReportSeverity.Info ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyText(NotificationEntry notification)
    {
        MessageText.Text = notification.Content.Message;

        ArrivalText.Text = FormatArrival(notification.ArrivedAt);

        if (notification.OccurrenceCount > 1)
        {
            OccurrenceText.Text = _stringLocalizer.GetString("NotificationCentre_Occurrences", notification.OccurrenceCount);
            OccurrenceText.Visibility = Visibility.Visible;
        }
    }

    private void ApplyAction(OpenDocumentAction? action)
    {
        if (action is null)
        {
            return;
        }

        ActionButton.Content = action.Label;
        ActionButton.Visibility = Visibility.Visible;
    }

    private void ApplyDismiss(NotificationKind kind)
    {
        if (kind != NotificationKind.Event)
        {
            return;
        }

        var dismissText = _stringLocalizer.GetString("NotificationCentre_Dismiss");
        ToolTipService.SetToolTip(DismissButton, dismissText);
        AutomationProperties.SetName(DismissButton, dismissText);

        DismissButton.Visibility = Visibility.Visible;
    }

    // The time alone for a notification from today, which is nearly all of them. One left pending from an
    // earlier day says which day.
    private static string FormatArrival(DateTimeOffset arrivedAt)
    {
        var localArrival = arrivedAt.ToLocalTime();

        var format = localArrival.Date == DateTime.Today
            ? "t"
            : "g";

        return localArrival.ToString(format, CultureInfo.CurrentCulture);
    }

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        var action = Notification.Content.Action;
        if (action is null)
        {
            return;
        }

        ActionRequested?.Invoke(action);
    }

    private void DismissButton_Click(object sender, RoutedEventArgs e)
    {
        DismissRequested?.Invoke(Notification);
    }
}
