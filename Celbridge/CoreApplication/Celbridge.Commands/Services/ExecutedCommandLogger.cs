using Celbridge.Utilities;
using Celbridge.Messaging;

namespace Celbridge.Commands.Services;

public class ExecutedCommandLogger : IExecutedCommandLogger, IDisposable
{
    private const string LogFilePrefix = "CommandLog";

    private readonly IMessengerService _messengerService;
    private readonly ILogSerializer _serializer;
    private readonly ILogger _logger;
    private readonly IUtilityService _utilityService;

    public ExecutedCommandLogger(
        IMessengerService messengerService,
        ILogSerializer logSerializer,
        ILogger logger,
        IUtilityService utilityService)
    {
        _messengerService = messengerService;
        _serializer = logSerializer;
        _logger = logger;
        _utilityService = utilityService;
    }

    public Result Initialize(string logFolderPath, int maxFilesToKeep)
    {
        var initResult = _logger.Initialize(logFolderPath, LogFilePrefix, maxFilesToKeep);
        if (initResult.IsFailure)
        {
            return initResult;
        }

        // Write environment info as the first record in the log
        var environmentInfo = _utilityService.GetEnvironmentInfo();
        var writeResult = WriteObject(environmentInfo);
        if (writeResult.IsFailure)
        {
            return writeResult;
        }

        // Start listening for executed commands
        _messengerService.Register<ExecutedCommandMessage>(this, OnExecutedCommandMessage);

        return Result.Ok();
    }
    private void OnExecutedCommandMessage(object recipient, ExecutedCommandMessage message)
    {
        WriteObject(message);
    }

    public Result WriteObject(object? obj)
    {
        if (obj is null)
        {
            return Result.Fail($"Object is null");
        }

        try
        {
            string logEntry = _serializer.SerializeObject(obj, false);
            var writeResult = _logger.WriteLine(logEntry);
            if (writeResult.IsFailure)
            {
                return writeResult;
            }
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to write object to log. {ex}");
        }

        return Result.Ok();
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
