namespace Celbridge.Notifications;

/// <summary>
/// Sent when the pending notifications change. HasArrival is true when a notification arrived, including one
/// counted on the identical entry before it.
/// </summary>
public record NotificationsChangedMessage(bool HasArrival);
