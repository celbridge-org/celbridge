using NLog.Targets;
using NLog;
using Celbridge.Messaging;
using Celbridge.Foundation;

namespace Celbridge.Logging.Services;

/// <summary>
/// A custom NSLog target that renders the logEvent to JSON and broadcasts it in a message.
/// </summary>
[Target("MessageTarget")]
public sealed class MessageTarget : TargetWithLayout
{
    protected override void Write(LogEventInfo logEvent)
    {
        // Format the log event as JSON
        string messageJson = this.Layout.Render(logEvent);
        if (string.IsNullOrEmpty(messageJson))
        {
            return;
        }

        IMessengerService messengerService = ServiceLocator.ServiceProvider.GetRequiredService<IMessengerService>();

        // Send the log event as a message to the console panel
        var message = new LogEventMessage(messageJson);
        messengerService.Send(message);
    }
}
