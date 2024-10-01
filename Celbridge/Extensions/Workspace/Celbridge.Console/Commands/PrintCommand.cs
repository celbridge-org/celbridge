using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Console;

public class PrintCommand : CommandBase, IPrintCommand
{
    private ILogger<PrintCommand> _logger;

    public string Message { get; set; } = string.Empty;

    public MessageType MessageType { get; set; } = MessageType.Information;

    public PrintCommand(
        ILogger<PrintCommand> logger,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
    }

    public override async Task<Result> ExecuteAsync()
    {
        switch (MessageType)
        {
            case MessageType.Command:
            case MessageType.Information:
                _logger.LogInformation(Message);
                break;
            case MessageType.Warning:
                _logger.LogWarning(Message);
                break;
            case MessageType.Error:
                _logger.LogError(Message);
                break;
        }

        await Task.CompletedTask;
        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void Print(string message)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IPrintCommand>(command =>
        {
            command.Message = message;
            command.MessageType = MessageType.Information;
        });
    }

    public static void PrintWarning(string message)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IPrintCommand>(command =>
        {
            command.Message = message;
            command.MessageType = MessageType.Warning;
        });
    }

    public static void PrintError(string message)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IPrintCommand>(command =>
        {
            command.Message = message;
            command.MessageType = MessageType.Error;
        });
    }
}
