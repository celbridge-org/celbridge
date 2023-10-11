using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Configuration;
using Celbridge.Services;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Utils
{
    public class ConsoleServiceEventSink : ILogEventSink
    {
        private readonly IFormatProvider _formatProvider;
        private IConsoleService _consoleService;

        public ConsoleServiceEventSink(IFormatProvider formatProvider, IConsoleService consoleService)
        {
            _formatProvider = formatProvider;
            _consoleService = consoleService;
        }

        public void Emit(LogEvent logEvent)
        {
            var message = logEvent.RenderMessage(_formatProvider);
            _consoleService.WriteMessage(message);
        }
    }

    public static class ConsoleServiceEventSinkExtensions
    {
        public static LoggerConfiguration ConsoleService(
            this LoggerSinkConfiguration loggerConfiguration,
            ConsoleService consoleService,
            IFormatProvider? formatProvider = null)
        {
            return loggerConfiguration.Sink(new ConsoleServiceEventSink(formatProvider!, consoleService));
        }
    }
}
