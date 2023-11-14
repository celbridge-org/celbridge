using Serilog.Core;
using Serilog.Events;
using Serilog.Configuration;
using Celbridge.Services;

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

            // Todo: Select the log type from a dropdown in the Print instruction
            var logType = ConsoleLogType.Info;
            var colonPos = message.IndexOf(':');
            if (colonPos != -1)
            {
                if (message.StartsWith("ok:"))
                {
                    message = message[3..].TrimStart();
                    logType = ConsoleLogType.Ok;
                }
                else if (message.StartsWith("error:"))
                {
                    message = message[6..].TrimStart();
                    logType = ConsoleLogType.Error;
                }
                else if (message.StartsWith("warn:"))
                {
                    message = message[5..].TrimStart();
                    logType = ConsoleLogType.Warn;
                }
            }
            else
            {
                switch (logEvent.Level)
                {
                    case LogEventLevel.Warning:
                        logType = ConsoleLogType.Warn;
                        break;
                    case LogEventLevel.Error:
                    case LogEventLevel.Fatal:
                        logType = ConsoleLogType.Error;
                        break;
                }
            }

            _consoleService.WriteMessage(message, logType);
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
