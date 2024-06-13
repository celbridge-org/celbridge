using Celbridge.BaseLibrary.UserInterface;
using Serilog;

namespace Celbridge.Services.Logging;

public class LoggingService : ILoggingService
{
    public LoggingService(IServiceProvider serviceProvider)
    {
        var config = new LoggerConfiguration()
            .WriteTo.Debug(); // Writes to the Visual Studio debug Output window (uses a Nuget package)

        // If a UserInterfaceService is available, write to the console panel
        var userInterfaceService = serviceProvider.GetService<IUserInterfaceService>();
        if (userInterfaceService is not null)
        {
            config.WriteTo.ConsoleService(userInterfaceService); // A custom log event sink that writes to the console panel
        }

        Log.Logger = config.CreateLogger();
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
