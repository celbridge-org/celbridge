using Serilog;

namespace Celbridge.Services.Logging;

public class LoggingService : ILoggingService
{
    private IMessengerService _messengerService;

    public LoggingService(IMessengerService messengerService)
    {
        _messengerService = messengerService;

        Log.Logger = new LoggerConfiguration()
            //.WriteTo.ConsoleService(this) // Our custom sink that writes to the Console panel in the app
            .WriteTo.Debug() // Writes to the Visual Studio debug Output window (uses a Nuget package)
        .CreateLogger();
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
