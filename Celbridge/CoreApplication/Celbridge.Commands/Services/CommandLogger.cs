using Celbridge.Messaging;
using Celbridge.Utilities;
using CommunityToolkit.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Celbridge.Commands.Services;

public class CommandLogger : ICommandLogger, IDisposable
{
    private record CommandLogItem(string CommandName, float ElapsedTime, CommandExecutionMode ExecutionMode, IExecutableCommand Command);

    private readonly IMessengerService _messengerService;
    private readonly IUtilityService _utilityService;

    private readonly JsonSerializerSettings _jsonSerializerSettings;

    private StreamWriter? _writer;

    public CommandLogger(
        IMessengerService messengerService,
        IUtilityService utilityService)
    {
        _messengerService = messengerService;
        _utilityService = utilityService;

        var ignoreProperties = new string[] { };
        var resolver = new CommandSerializerContractResolver(ignoreProperties);
        _jsonSerializerSettings = new JsonSerializerSettings
        {
            ContractResolver = resolver,
            Formatting = Formatting.None
        };

        _jsonSerializerSettings.Converters.Add(new StringEnumConverter());
        _jsonSerializerSettings.Converters.Add(new EntityIdConverter());
        _jsonSerializerSettings.Converters.Add(new ResourceKeyConverter());
    }

    public Result StartLogging(string logFilePath, string logFilePrefix, int maxFilesToKeep)
    {
        var logFolderPath = Path.GetDirectoryName(logFilePath)!;
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
        string logEntry = JsonConvert.SerializeObject(environmentInfo, _jsonSerializerSettings);
        _writer.WriteLine(logEntry);

        // Start listening for executing commands
        _messengerService.Register<ExecutedCommandMessage>(this, OnExecutedCommandMessage);

        return Result.Ok();
    }

    private void OnExecutedCommandMessage(object recipient, ExecutedCommandMessage message)
    {
        var command = message.Command;

        var commandLogItem = new CommandLogItem(command.GetType().Name, message.ElapsedTime, message.ExecutionMode, command);
        string logEntry = JsonConvert.SerializeObject(commandLogItem, _jsonSerializerSettings);

        Guard.IsNotNull(_writer);
        _writer.WriteLineAsync(logEntry);
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
