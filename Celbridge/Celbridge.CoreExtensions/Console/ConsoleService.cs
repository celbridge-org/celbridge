using Celbridge.BaseLibrary.Console;
using Celbridge.BaseLibrary.Logging;
using CommunityToolkit.Mvvm.Messaging;

namespace Celbridge.CoreExtensions.Console;

public class ConsoleService : IConsoleService
{
    private IMessenger _messenger;
    private ILoggingService _loggingService;

    public ConsoleService(IMessenger messenger, ILoggingService loggingService)
    {
        _messenger = messenger;
        _loggingService = loggingService;
    }

    public void Execute(string command)
    {
        var logMessage = $"Command: {command}";

        _loggingService.Info(logMessage);

        var message = new WroteToLogMessage(logMessage);
        _messenger.Send(message);
    }
}
