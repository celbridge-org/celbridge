using Celbridge.BaseLibrary.Console;
using Celbridge.BaseLibrary.Logging;
using Celbridge.BaseLibrary.Messaging;

namespace Celbridge.CoreExtensions.Console;

public class ConsoleService : IConsoleService
{
    private IMessengerService _messengerService;
    private ILoggingService _loggingService;

    public ConsoleService(IMessengerService messengerService, ILoggingService loggingService)
    {
        _messengerService = messengerService;
        _loggingService = loggingService;
    }

    public async Task<Result> Execute(string command)
    {
        if (command == "print")
        {
            // Simulate an async delay
            await Task.Delay(100);

            var logMessage = $"print: hello!";
            _loggingService.Info(logMessage);
            return Result.Ok();
        }

        return Result.Fail($"Unknown command: {command}");
    }

    public string GetTestString()
    {
        return "Text from console service";
    }
}
