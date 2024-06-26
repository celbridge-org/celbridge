using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Configuration;
using Celbridge.BaseLibrary.Console;
using Celbridge.BaseLibrary.Workspace;

namespace Celbridge.Logging.Services;

/// <summary>
/// A custom Serilog event sink that writes log messages to the console panel (if one is available).
/// </summary>
public class ConsoleLogEventSink : ILogEventSink
{
    private readonly IFormatProvider _formatProvider;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ConsoleLogEventSink(IFormatProvider formatProvider, IWorkspaceWrapper workspaceWrapper)
    {
        _formatProvider = formatProvider;
        _workspaceWrapper = workspaceWrapper;
    }

    public void Emit(LogEvent logEvent)
    {
        if (!_workspaceWrapper.IsWorkspaceLoaded)
        {
            return;
        }

        var consoleService = _workspaceWrapper.WorkspaceService.ConsoleService;

        var message = logEvent.RenderMessage(_formatProvider);
        consoleService.Print(MessageType.Info, message);
    }
}

public static class ConsoleServiceEventSinkExtensions
{
    public static LoggerConfiguration ConsoleService(
        this LoggerSinkConfiguration loggerConfiguration,
        IWorkspaceWrapper workspaceWrapper,
        IFormatProvider? formatProvider = null)
    {
        return loggerConfiguration.Sink(new ConsoleLogEventSink(formatProvider!, workspaceWrapper));
    }
}
