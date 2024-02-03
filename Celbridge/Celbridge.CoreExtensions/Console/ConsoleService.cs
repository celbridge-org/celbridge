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

    public async Task<Result> Execute(string command)
    {
        if (command == "print")
        {
            // Simulate an async delay
            await Task.Delay(500);

            var logMessage = $"print: hello!";
            _loggingService.Info(logMessage);
            return Result.Ok();
        }

        return Result.Fail($"Unknown command: {command}");
    }
}
