using Celbridge.Notifications;

namespace Celbridge.UserInterface.Services;

public sealed class NotificationCentre : INotificationCentre
{
    /// <summary>
    /// The most notifications the centre holds at once. Past it the oldest event is dropped to make room.
    /// </summary>
    public const int NotificationLimit = 50;

    private record PendingCondition(string Source, NotificationEntry Entry);

    private readonly IMessengerService _messengerService;

    private readonly object _lock = new();

    // Both newest first, which is the order they are listed in.
    private readonly List<PendingCondition> _conditions = new();
    private readonly List<NotificationEntry> _events = new();

    private IReadOnlyList<NotificationEntry> _notifications = Array.Empty<NotificationEntry>();

    private long _nextNotificationId = 1;

    public NotificationCentre(IMessengerService messengerService)
    {
        _messengerService = messengerService;
    }

    public IReadOnlyList<NotificationEntry> Notifications
    {
        get
        {
            lock (_lock)
            {
                return _notifications;
            }
        }
    }

    public void RecordCondition(string source, NotificationContent content)
    {
        lock (_lock)
        {
            var conditionIndex = _conditions.FindIndex(condition => condition.Source == source);
            if (conditionIndex >= 0)
            {
                // Recording what is already pending changes nothing the user can see, so it is not an arrival.
                if (_conditions[conditionIndex].Entry.Content == content)
                {
                    return;
                }

                _conditions.RemoveAt(conditionIndex);
            }
            else
            {
                MakeRoom();
            }

            var entry = CreateEntry(NotificationKind.Condition, content);
            _conditions.Insert(0, new PendingCondition(source, entry));

            UpdateNotifications();
        }

        SendNotificationsChanged(hasArrival: true);
    }

    public void AddEvent(NotificationContent content)
    {
        lock (_lock)
        {
            if (_events.Count > 0 &&
                _events[0].Content == content)
            {
                var newestEvent = _events[0];

                _events[0] = newestEvent with
                {
                    ArrivedAt = DateTimeOffset.UtcNow,
                    OccurrenceCount = newestEvent.OccurrenceCount + 1
                };
            }
            else
            {
                MakeRoom();

                var entry = CreateEntry(NotificationKind.Event, content);
                _events.Insert(0, entry);
            }

            UpdateNotifications();
        }

        SendNotificationsChanged(hasArrival: true);
    }

    public void Dismiss(long notificationId)
    {
        lock (_lock)
        {
            var removedCount = _events.RemoveAll(entry => entry.Id == notificationId);
            if (removedCount == 0)
            {
                return;
            }

            UpdateNotifications();
        }

        SendNotificationsChanged(hasArrival: false);
    }

    public void ClearEvents()
    {
        lock (_lock)
        {
            if (_events.Count == 0)
            {
                return;
            }

            _events.Clear();

            UpdateNotifications();
        }

        SendNotificationsChanged(hasArrival: false);
    }

    public void Clear()
    {
        lock (_lock)
        {
            if (_conditions.Count == 0 &&
                _events.Count == 0)
            {
                return;
            }

            _conditions.Clear();
            _events.Clear();

            UpdateNotifications();
        }

        SendNotificationsChanged(hasArrival: false);
    }

    // Only events make room. A condition still holds, and there is at most one per source.
    private void MakeRoom()
    {
        while (_conditions.Count + _events.Count >= NotificationLimit &&
            _events.Count > 0)
        {
            _events.RemoveAt(_events.Count - 1);
        }
    }

    private NotificationEntry CreateEntry(NotificationKind kind, NotificationContent content)
    {
        var notificationId = _nextNotificationId;
        _nextNotificationId++;

        return new NotificationEntry(notificationId, kind, content, DateTimeOffset.UtcNow, OccurrenceCount: 1);
    }

    // Each change publishes a new list, since a reader on another thread may still hold the previous one.
    private void UpdateNotifications()
    {
        var notifications = new List<NotificationEntry>(_conditions.Count + _events.Count);

        foreach (var condition in _conditions)
        {
            notifications.Add(condition.Entry);
        }

        notifications.AddRange(_events);

        _notifications = notifications.AsReadOnly();
    }

    // Sent outside the lock, so a recipient reading the list back cannot deadlock with a change on another
    // thread.
    private void SendNotificationsChanged(bool hasArrival)
    {
        var message = new NotificationsChangedMessage(hasArrival);
        _messengerService.Send(message);
    }
}
