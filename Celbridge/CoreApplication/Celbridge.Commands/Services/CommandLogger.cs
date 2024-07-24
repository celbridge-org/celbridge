using Celbridge.Messaging;
using Celbridge.Utilities;

namespace Celbridge.Commands.Services;

public class CommandLogger : ICommandLogger, IDisposable
{
    private const string LogFilePrefix = "CommandLog";

    private readonly IMessengerService _messengerService;
    private readonly IUtilityService _utilityService;
    private readonly ICommandLogSerializer _serializer;

    private string _logFilePath = string.Empty;

    public CommandLogger(
        IMessengerService messengerService,
        IUtilityService utilityService,
        ICommandLogSerializer commandLogSerializer)
    {
        _messengerService = messengerService;
        _utilityService = utilityService;
        _serializer = commandLogSerializer;
    }

    public Result Start(string logFolderPath, int maxFilesToKeep)
    {
        // Aqcuire the log folder
        if (Directory.Exists(logFolderPath))
        {
            // Delete old log files that start with the same prefix
            var deleteResult = _utilityService.DeleteOldFiles(logFolderPath, LogFilePrefix, maxFilesToKeep);
            if (deleteResult.IsFailure)
            {
                return deleteResult;
            }
        }
        else
        {
            Directory.CreateDirectory(logFolderPath);
        }

        // Generate the log file path
        var timestamp = _utilityService.GetTimestamp();
        var logFilename = $"{LogFilePrefix}_{timestamp}.jsonl";
        _logFilePath = Path.Combine(logFolderPath, logFilename);

        // Write environment info as the first record in the log
        var environmentInfo = _utilityService.GetEnvironmentInfo();
        string logEntry = _serializer.SerializeObject(environmentInfo, false);
        var writeResult = WriteLine(logEntry);
        if (writeResult.IsFailure)
        {
            return writeResult;
        }

        // Start listening for executed commands
        _messengerService.Register<ExecutedCommandMessage>(this, OnExecutedCommandMessage);

        return Result.Ok();
    }

    private Result WriteLine(string line)
    {
        try
        {
            using (var fileStream = new FileStream(_logFilePath, FileMode.Append, FileAccess.Write))
            using (var writer = new StreamWriter(fileStream))
            {
                // Write the log message with a newline character
                writer.WriteLine(line);
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to write to log. {ex}");
        }
    }

    private void OnExecutedCommandMessage(object recipient, ExecutedCommandMessage message)
    {
        string serialized = _serializer.SerializeObject(message, false);
        WriteLine(serialized);
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

    ~CommandLogger()
    {
        Dispose(false);
    }
}
