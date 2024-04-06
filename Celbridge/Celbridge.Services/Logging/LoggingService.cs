using Serilog;

namespace Celbridge.Services.Logging;

public class LoggingService : ILoggingService
{
    public LoggingService()
    {
        Log.Logger = new LoggerConfiguration()
            //.WriteTo.ConsoleService(this) // Our custom sink that writes to the Console panel in the app
            .WriteTo.Debug() // Writes to the Visual Studio debug Output window (uses a Nuget package)
        .CreateLogger();
    }

    public void Info(string logMessage)
    {
        Log.Information(logMessage);

        var message = new WroteLogMessage(LogMessageType.Info, logMessage);
    }

    public void Warn(string logMessage)
    {
        Log.Warning(logMessage);

        var message = new WroteLogMessage(LogMessageType.Warning, logMessage);
    }

    public void Error(string logMessage)
    {
        Log.Error(logMessage);

        var message = new WroteLogMessage(LogMessageType.Error, logMessage);
    }
}
