using Celbridge.Utilities;
using Celbridge.Messaging;

namespace Celbridge.Commands.Services;

public class ExecutedCommandLogger : IExecutedCommandLogger, IDisposable
{
    private const string LogFilePrefix = "CommandLog";

    private readonly IMessengerService _messengerService;
    private readonly ILogger _logger;

    public ExecutedCommandLogger(
        IMessengerService messengerService,
        ILogger logger)
    {
        _messengerService = messengerService;
        _logger = logger;
    }

    public Result Initialize(string logFolderPath, int maxFilesToKeep)
    {
        var initResult = _logger.Initialize(logFolderPath, LogFilePrefix, maxFilesToKeep);
        if (initResult.IsFailure)
        {
            return initResult;
        }

        // Start listening for executed commands
        _messengerService.Register<ExecutedCommandMessage>(this, OnExecutedCommandMessage);

        return Result.Ok();
    }
    private void OnExecutedCommandMessage(object recipient, ExecutedCommandMessage message)
    {
        _logger.WriteObject(message);
    }

    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed objects here
                _messengerService.Unregister<ExecutedCommandMessage>(this);
            }

            _disposed = true;
        }
    }

    ~ExecutedCommandLogger()
    {
        Dispose(false);
    }
}
