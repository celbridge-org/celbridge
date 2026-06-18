using Celbridge.Dialog;
using Celbridge.Logging;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Holds the pending automated-answer slot used by IDialogService.ScheduleAnswer.
/// When a dialog of the scheduled kind is displayed the slot is consumed and a
/// DialogAnswerMessage is broadcast after the requested delay; subscribed
/// dialogs receive the message and self-close with the affirmative response.
/// </summary>
internal sealed class DialogAnswerScheduler
{
    private readonly ILogger _logger;
    private readonly IMessengerService _messengerService;
    private readonly object _lock = new();
    private bool _set;
    private DialogKind _dialogKind;
    private string _payload = string.Empty;
    private int _delayMs;

    public DialogAnswerScheduler(
        ILogger logger,
        IMessengerService messengerService)
    {
        _logger = logger;
        _messengerService = messengerService;
    }

    public void Schedule(DialogKind dialogKind, string payload, int delayMs)
    {
        lock (_lock)
        {
            _set = true;
            _dialogKind = dialogKind;
            _payload = payload;
            _delayMs = delayMs;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _set = false;
            _dialogKind = default;
            _payload = string.Empty;
            _delayMs = 0;
        }
    }

    /// <summary>
    /// Notify the scheduler that a dialog of the given kind is about to display.
    /// A matching pending schedule is consumed and the answer broadcast after
    /// the delay; a non-matching schedule stays pending and the dialog shows
    /// normally.
    /// </summary>
    public void OnDialogShown(DialogKind dialogKind)
    {
        string payload;
        int delayMs;
        lock (_lock)
        {
            if (!_set)
            {
                return;
            }

            if (_dialogKind != dialogKind)
            {
                _logger.LogWarning(
                    $"Dialog '{dialogKind}' is being shown but a scheduled answer is pending for '{_dialogKind}'. Leaving the schedule in place.");
                return;
            }

            payload = _payload;
            delayMs = _delayMs;
            _set = false;
            _dialogKind = default;
            _payload = string.Empty;
            _delayMs = 0;
        }

        _ = DelayThenBroadcastAsync(dialogKind, payload, delayMs);
    }

    private async Task DelayThenBroadcastAsync(DialogKind dialogKind, string payload, int delayMs)
    {
        // The await captures the calling SynchronizationContext (the UI thread
        // when OnDialogShown is invoked from a Show* method), so the broadcast
        // and any Hide() calls in dialog message handlers resume on the UI
        // thread without explicit marshalling.
        try
        {
            // Yield unconditionally before broadcasting. OnDialogShown runs
            // before the dialog's Show* method registers its handler, so a
            // delayMs of 0 would otherwise Send synchronously here, before any
            // dialog is listening, and the answer would be lost.
            await Task.Yield();
            if (delayMs > 0)
            {
                await Task.Delay(delayMs);
            }
            _messengerService.Send(new DialogAnswerMessage(dialogKind, payload));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scheduled dialog answer broadcast failed.");
        }
    }
}
