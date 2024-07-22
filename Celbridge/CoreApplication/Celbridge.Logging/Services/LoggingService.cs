using Celbridge.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Celbridge.Logging.Services;

public class LoggingService : ILoggingService
{
    public LoggingService(IServiceProvider serviceProvider)
    {
        var config = new LoggerConfiguration()
            .WriteTo.Debug(); // Writes to the Visual Studio debug Output window (uses a Nuget package)

        // If a WorkspaceService is laoded, output log messages to the console panel
        var workspaceWrapper = serviceProvider.GetService<IWorkspaceWrapper>();
        if (workspaceWrapper is not null)
        {
            config.WriteTo.ConsoleService(workspaceWrapper); // A custom log event sink that writes to the console panel
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
