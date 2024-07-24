using Celbridge.Messaging;
using Celbridge.Utilities;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Commands.Services;

public class CommandLogger : ICommandLogger, IDisposable
{
    private const string LogFilePrefix = "CommandLog";

    private readonly IMessengerService _messengerService;
    private readonly IUtilityService _utilityService;
    private readonly ICommandLogSerializer _commandLogSerializer;

    private StreamWriter? _writer;

    public CommandLogger(
        IMessengerService messengerService,
        IUtilityService utilityService,
        ICommandLogSerializer commandLogSerializer)
    {
        _messengerService = messengerService;
        _utilityService = utilityService;
        _commandLogSerializer = commandLogSerializer;
    }

    public Result Start(string logFolderPath, int maxFilesToKeep)
    {
        var timestamp = _utilityService.GetTimestamp();
        string logFilePrefix = LogFilePrefix;
        string logFilename = $"{logFilePrefix}_{timestamp}.jsonl";
        string logFilePath = Path.Combine(logFolderPath, logFilename);

        if (!Directory.Exists(logFolderPath))
        {
            Directory.CreateDirectory(logFolderPath);
        }

        // Delete old log files

        var deleteResult = _utilityService.DeleteOldFiles(logFolderPath, logFilePrefix, maxFilesToKeep);
        if (deleteResult.IsFailure)
        {
            return deleteResult;
        }

        _writer = new StreamWriter(logFilePath, append: true) 
        { 
            AutoFlush = true 
        };

        // Write environment info as the first record in the log

        var environmentInfo = _utilityService.GetEnvironmentInfo();
        string logEntry = _commandLogSerializer.SerializeObject(environmentInfo, false);
        _writer.WriteLine(logEntry);

        // Start listening for executed commands
        _messengerService.Register<ExecutedCommandMessage>(this, OnExecutedCommandMessage);

        return Result.Ok();
    }

    private void OnExecutedCommandMessage(object recipient, ExecutedCommandMessage message)
    {
        string serialized = _commandLogSerializer.SerializeObject(message, false);

        Guard.IsNotNull(_writer);
        _writer.WriteLineAsync(serialized);
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

                _writer?.Dispose();
            }

            _disposed = true;
        }
    }

    ~CommandLogger()
    {
        Dispose(false);
    }
}
