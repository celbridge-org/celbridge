namespace Celbridge.CommonServices;

[CelService(CelServiceLifetime.Singleton)]
public class LoggingService : ILoggingService
{
    public void Log(string message)
    {
        Console.WriteLine(message);
    }
}
