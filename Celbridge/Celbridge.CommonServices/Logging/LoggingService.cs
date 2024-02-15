using Serilog;

namespace Celbridge.CommonServices.Logging;

public class LoggingService : ILoggingService
{
    private IMessengerService _messengerService;

    public LoggingService(IMessengerService messengerService)
    {
        _messengerService = messengerService;
    }

    public void Info(string logMessage)
    {
        Log.Information(logMessage);

        var message = new WroteLogMessage(LogMessageType.Info, logMessage);
        _messengerService.Send(message);

    }

    public void Warn(string logMessage)
    {
        Log.Warning(logMessage);

        var message = new WroteLogMessage(LogMessageType.Warning, logMessage);
        _messengerService.Send(message);
    }

    public void Error(string logMessage)
    {
        Log.Error(logMessage);

        var message = new WroteLogMessage(LogMessageType.Error, logMessage);
        _messengerService.Send(message);
    }
}
