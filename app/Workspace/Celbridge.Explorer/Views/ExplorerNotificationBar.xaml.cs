using Celbridge.Logging;
using Microsoft.Extensions.Localization;
using Microsoft.UI.Dispatching;

namespace Celbridge.Explorer.Views;

/// <summary>
/// A notification bar that displays resource operation notifications.
/// Auto-dismisses after a configurable delay.
/// </summary>
public sealed partial class ExplorerNotificationBar : UserControl
{
    private const double AutoDismissDelaySeconds = 10.0;

    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ILogger<ExplorerNotificationBar> _logger;
    private readonly DispatcherQueueTimer _autoDismissTimer;

    public ExplorerNotificationBar()
    {
        _messengerService = ServiceLocator.AcquireService<IMessengerService>();
        _stringLocalizer = ServiceLocator.AcquireService<IStringLocalizer>();
        _logger = ServiceLocator.AcquireService<ILogger<ExplorerNotificationBar>>();

        InitializeComponent();

        // Setup auto-dismiss timer
        _autoDismissTimer = DispatcherQueue.CreateTimer();
        _autoDismissTimer.Interval = TimeSpan.FromSeconds(AutoDismissDelaySeconds);
        _autoDismissTimer.IsRepeating = false;
        _autoDismissTimer.Tick += AutoDismissTimer_Tick;

        // Subscribe to operation failed messages
        _messengerService.Register<ResourceOperationFailedMessage>(this, OnResourceOperationFailed);

        Unloaded += ExplorerNotificationBar_Unloaded;
    }

    private void ExplorerNotificationBar_Unloaded(object sender, RoutedEventArgs e)
    {
        _autoDismissTimer.Stop();
        _messengerService.UnregisterAll(this);
    }

    private void OnResourceOperationFailed(object recipient, ResourceOperationFailedMessage message)
    {
        var localizedMessage = GetLocalizedMessage(message.OperationType, message.FailedItems);

        _logger.LogDebug($"Showing notification: {localizedMessage}");

        NotificationInfoBar.Message = localizedMessage;
        NotificationInfoBar.IsOpen = true;

        // Restart auto-dismiss timer
        _autoDismissTimer.Stop();
        _autoDismissTimer.Start();
    }

    private string GetLocalizedMessage(ResourceOperationType operationType, List<string> failedItems)
    {
        const int maxItemsToShow = 3;

        string failedList;
        if (failedItems.Count <= maxItemsToShow)
        {
            failedList = string.Join(", ", failedItems);
        }
        else
        {
            failedList = string.Join(", ", failedItems.Take(maxItemsToShow)) + "…";
        }

        var messageKey = operationType switch
        {
            ResourceOperationType.Delete => "Explorer_OperationFailed_Delete",
            ResourceOperationType.Copy => "Explorer_OperationFailed_Copy",
            ResourceOperationType.Move => "Explorer_OperationFailed_Move",
            ResourceOperationType.Rename => "Explorer_OperationFailed_Rename",
            ResourceOperationType.Create => "Explorer_OperationFailed_Create",
            _ => "Explorer_OperationFailed_Unknown"
        };

        return _stringLocalizer.GetString(messageKey, failedList);
    }

    private void AutoDismissTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        NotificationInfoBar.IsOpen = false;
    }

    private void NotificationInfoBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        _autoDismissTimer.Stop();
    }
}
