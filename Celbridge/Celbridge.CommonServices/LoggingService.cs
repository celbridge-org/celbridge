namespace Celbridge.CommonServices;
using Serilog;

[CelService(CelServiceLifetime.Singleton)]
public class LoggingService : ILoggingService
{
    public void Info(string message)
    {
        Log.Information(message);
    }

    public void Warn(string message)
    {
        Log.Warning(message);
    }

    public void Error(string message)
    {
        Log.Error(message);
    }
}
