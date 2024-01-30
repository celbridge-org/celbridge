namespace Celbridge.CoreExtensions;

public class ConsoleService : IConsoleService
{
    private ILoggingService _loggingService;

    public ConsoleService(ILoggingService loggingService)
    {
        _loggingService = loggingService;            
    }

    public void Execute(string command)
    {
        _loggingService.Log($"Received: {command}");
    }
}
