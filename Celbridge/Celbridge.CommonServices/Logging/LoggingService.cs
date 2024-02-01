namespace Celbridge.CommonServices.Logging;

using Celbridge.BaseLibrary.Logging;
using CommunityToolkit.Mvvm.Messaging;

public class LoggingService : ILoggingService
{
    private IMessenger _messenger;

    public LoggingService(IMessenger messenger)
    {
        _messenger = messenger;
    }

    public void Info(string logMessage)
    {
        Log.Information(logMessage);

        var message = new WroteToLogMessage(logMessage);
        _messenger.Send(message);

    }

    public void Warn(string logMessage)
    {
        Log.Warning(logMessage);

        var message = new WroteToLogMessage(logMessage);
        _messenger.Send(message);
    }

    public void Error(string logMessage)
    {
        Log.Error(logMessage);

        var message = new WroteToLogMessage(logMessage);
        _messenger.Send(message);
    }
}
