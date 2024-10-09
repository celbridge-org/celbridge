using Celbridge.Commands;
using Celbridge.Foundation;
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
                // Command log entries have a specific icon in the console log.
                // If the user attempts to print using MessageType.Command we just map it to MessageType.Information instead.
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

    public static void Print(object message)
    {
        Print(MessageType.Information, message);
    }

    public static void PrintWarning(object message)
    {
        Print(MessageType.Warning, message);
    }

    public static void PrintError(object message)
    {
        Print(MessageType.Error, message);
    }

    private static void Print(MessageType messageType, object message)
    {
        var messageText = message.ToString() ?? string.Empty;
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IPrintCommand>(command =>
        {
            command.Message = messageText;
            command.MessageType = messageType;
        });
    }
}
