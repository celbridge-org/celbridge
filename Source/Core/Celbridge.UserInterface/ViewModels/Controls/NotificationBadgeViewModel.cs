using Celbridge.Commands;
using Celbridge.Documents;
using Celbridge.Localization;
using Celbridge.Notifications;
using Celbridge.Reports;
using Celbridge.Utilities;

namespace Celbridge.UserInterface.ViewModels.Controls;

/// <summary>
/// Drives the notification badge and the list it opens: what is pending, the severity and summary the badge
/// states, and what the user can do about each entry.
/// </summary>
public class NotificationBadgeViewModel
{
    private readonly IMessengerService _messengerService;
    private readonly IDispatcher _dispatcher;
    private readonly ILocalizerService _localizerService;
    private readonly INotificationCentre _notificationCentre;
    private readonly ICommandService _commandService;

    /// <summary>
    /// Raised on the UI thread when the pending notifications change.
    /// </summary>
    public event EventHandler? NotificationsChanged;

    /// <summary>
    /// Raised on the UI thread when a notification arrives, after the change it makes has been raised.
    /// </summary>
    public event EventHandler? NotificationArrived;

    /// <summary>
    /// The pending notifications, in the order they are listed.
    /// </summary>
    public IReadOnlyList<NotificationEntry> Notifications { get; private set; } = Array.Empty<NotificationEntry>();

    /// <summary>
    /// The most serious severity among the pending notifications.
    /// </summary>
    public ReportSeverity Severity { get; private set; } = ReportSeverity.Info;

    /// <summary>
    /// What the badge's tooltip and accessible name say about the pending notifications.
    /// </summary>
    public string Summary { get; private set; } = string.Empty;

    /// <summary>
    /// Whether any pending notification is an event, and so something Clear All would remove.
    /// </summary>
    public bool HasEvents { get; private set; }

    public NotificationBadgeViewModel(
        IMessengerService messengerService,
        IDispatcher dispatcher,
        ILocalizerService localizerService,
        INotificationCentre notificationCentre,
        ICommandService commandService)
    {
        _messengerService = messengerService;
        _dispatcher = dispatcher;
        _localizerService = localizerService;
        _notificationCentre = notificationCentre;
        _commandService = commandService;
    }

    public void OnLoaded()
    {
        _messengerService.Register<NotificationsChangedMessage>(this, OnNotificationsChanged);

        Refresh();
    }

    public void OnUnloaded()
    {
        _messengerService.UnregisterAll(this);
    }

    /// <summary>
    /// Opens the document an action names, leaving the notification pending.
    /// </summary>
    public void PerformAction(OpenDocumentAction action)
    {
        _commandService.Execute<IOpenDocumentCommand>(command =>
        {
            command.FileResource = action.Resource;
            command.Location = DocumentLocation.Compose(action.Line, action.Column);
        });
    }

    public void Dismiss(NotificationEntry notification)
    {
        _notificationCentre.Dismiss(notification.Id);
    }

    public void ClearEvents()
    {
        _notificationCentre.ClearEvents();
    }

    private void OnNotificationsChanged(object recipient, NotificationsChangedMessage message)
    {
        // Sent from whichever thread recorded the change.
        _dispatcher.TryEnqueue(() =>
        {
            Refresh();

            NotificationsChanged?.Invoke(this, EventArgs.Empty);

            if (message.HasArrival)
            {
                NotificationArrived?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    private void Refresh()
    {
        var notifications = _notificationCentre.Notifications;

        Notifications = notifications;
        Severity = ResolveSeverity(notifications);
        Summary = ComposeSummary(notifications);
        HasEvents = notifications.Any(notification => notification.Kind == NotificationKind.Event);
    }

    private static ReportSeverity ResolveSeverity(IReadOnlyList<NotificationEntry> notifications)
    {
        var severity = ReportSeverity.Info;

        foreach (var notification in notifications)
        {
            if (notification.Content.Severity > severity)
            {
                severity = notification.Content.Severity;
            }
        }

        return severity;
    }

    // A lone notification is summarised by its own line, and several by a count of each severity.
    private string ComposeSummary(IReadOnlyList<NotificationEntry> notifications)
    {
        if (notifications.Count == 0)
        {
            return string.Empty;
        }

        if (notifications.Count == 1)
        {
            return notifications[0].Content.Message;
        }

        var errorCount = notifications.Count(notification => notification.Content.Severity == ReportSeverity.Error);
        var warningCount = notifications.Count(notification => notification.Content.Severity == ReportSeverity.Warning);
        var messageCount = notifications.Count(notification => notification.Content.Severity == ReportSeverity.Info);

        var sentences = new List<string>();

        AddCountSentence(sentences, errorCount, "NotificationCentre_Summary_Errors");
        AddCountSentence(sentences, warningCount, "NotificationCentre_Summary_Warnings");
        AddCountSentence(sentences, messageCount, "NotificationCentre_Summary_Messages");

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
