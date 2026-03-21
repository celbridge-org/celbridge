using Celbridge.Broker;
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
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IPrintCommand>(command =>
        {
            command.Message = messageText;
            command.MessageType = messageType;
        });
    }

    //
    // Broker tool methods.
    //

    [McpTool(Name = "console/log", Alias = "log", Description = "Logs a message to the console")]
    public static void BrokerLog(
        [McpParam(Description = "The message to log")]
        string message)
    {
        Print(MessageType.Information, message);
    }

    [McpTool(Name = "console/log_warning", Alias = "log_warning", Description = "Logs a warning message to the console")]
    public static void BrokerLogWarning(
        [McpParam(Description = "The warning message to log")]
        string message)
    {
        Print(MessageType.Warning, message);
    }

    [McpTool(Name = "console/log_error", Alias = "log_error", Description = "Logs an error message to the console")]
    public static void BrokerLogError(
        [McpParam(Description = "The error message to log")]
        string message)
    {
        Print(MessageType.Error, message);
    }
}
