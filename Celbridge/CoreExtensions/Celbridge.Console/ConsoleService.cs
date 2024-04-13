using Celbridge.BaseLibrary.Console;

namespace Celbridge.Console;

public class ConsoleService : IConsoleService
{
    private IMessengerService _messengerService;
    private ILoggingService _loggingService;

    public ConsoleService(IMessengerService messengerService, ILoggingService loggingService)
    {
        _messengerService = messengerService;
        _loggingService = loggingService;
    }
}
