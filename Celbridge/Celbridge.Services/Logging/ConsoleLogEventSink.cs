using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Configuration;
using Celbridge.BaseLibrary.Console;
using Celbridge.BaseLibrary.UserInterface;

namespace Celbridge.Services.Logging;

/// <summary>
/// A custom Serilog event sink that writes log messages to the console panel (if one is available).
/// </summary>
public class ConsoleLogEventSink : ILogEventSink
{
    private readonly IFormatProvider _formatProvider;
    private readonly IUserInterfaceService _userInterfaceService;

    public ConsoleLogEventSink(IFormatProvider formatProvider, IUserInterfaceService userInterfaceService)
    {
        _formatProvider = formatProvider;
        _userInterfaceService = userInterfaceService;
    }

    public void Emit(LogEvent logEvent)
    {
        if (!_userInterfaceService.IsWorkspaceLoaded)
        {
            return;
        }

        var consoleService = _userInterfaceService.WorkspaceService.ConsoleService;

        var message = logEvent.RenderMessage(_formatProvider);
        consoleService.Print(MessageType.Info, message);
    }
}

public static class ConsoleServiceEventSinkExtensions
{
    public static LoggerConfiguration ConsoleService(
        this LoggerSinkConfiguration loggerConfiguration,
        IUserInterfaceService userInterfaceService,
        IFormatProvider? formatProvider = null)
    {
        return loggerConfiguration.Sink(new ConsoleLogEventSink(formatProvider!, userInterfaceService));
    }
}
