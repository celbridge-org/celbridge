namespace Celbridge.Notifications;

/// <summary>
/// Sent when the pending notifications change. HasArrival is true when a notification arrived, including one
/// counted on the identical entry before it.
/// </summary>
public record NotificationsChangedMessage(bool HasArrival);

/// <summary>
/// Holds the notifications waiting for the user while a project is loaded. Safe to call from any thread.
/// </summary>
public interface INotificationCentre
{
    /// <summary>
    /// The pending notifications in the order they are listed: conditions first, then events, each newest
    /// first.
    /// </summary>
    IReadOnlyList<NotificationEntry> Notifications { get; }

    /// <summary>
    /// Records the condition a source reports, replacing the condition that source recorded before.
    /// </summary>
    void RecordCondition(string source, NotificationContent content);

    /// <summary>
    /// Adds an event. An event identical to the newest one adds to that entry's occurrence count, and the oldest
    /// event is dropped once the list is full.
    /// </summary>
    void AddEvent(NotificationContent content);

    /// <summary>
    /// Dismisses the event with the given id. A condition cannot be dismissed, so naming one does nothing.
    /// </summary>
    void Dismiss(long notificationId);

    /// <summary>
    /// Dismisses every event, leaving the conditions.
    /// </summary>
    void ClearEvents();

    /// <summary>
    /// Removes every notification, conditions included.
    /// </summary>
    void Clear();
}
