using Celbridge.BaseLibrary.Console;
using Celbridge.BaseLibrary.UserInterface;
using Serilog;

namespace Celbridge.Services.Logging;

public class LoggingService : ILoggingService
{
    public LoggingService(IUserInterfaceService userInterfaceService)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.ConsoleService(userInterfaceService) // A custom log event sink that writes to the console panel
            .WriteTo.Debug() // Writes to the Visual Studio debug Output window (uses a Nuget package)
        .CreateLogger();
    }

    public void Info(string logMessage)
    {
        Log.Information(logMessage);
    }

    public void Warn(string logMessage)
    {
        Log.Warning(logMessage);
    }

    public void Error(string logMessage)
    {
        Log.Error(logMessage);
    }
}
