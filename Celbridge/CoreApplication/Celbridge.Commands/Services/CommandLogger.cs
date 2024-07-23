using Celbridge.Messaging;
using Celbridge.Utilities;
using CommunityToolkit.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Celbridge.Commands.Services;

public class CommandLogger : ICommandLogger, IDisposable
{
    private const string LogFilePrefix = "CommandLog";

    private record CommandItem(string CommandName, float ElapsedTime, CommandExecutionMode ExecutionMode, IExecutableCommand Command);

    private readonly IMessengerService _messengerService;
    private readonly IUtilityService _utilityService;

    private readonly JsonSerializerSettings _unfilteredSettings;
    private readonly JsonSerializerSettings _filteredSettings;

    private StreamWriter? _writer;

    public CommandLogger(
        IMessengerService messengerService,
        IUtilityService utilityService)
    {
        _messengerService = messengerService;
        _utilityService = utilityService;

        _unfilteredSettings = CreateJsonSettings(false);
        _filteredSettings = CreateJsonSettings(true);
    }

    private JsonSerializerSettings CreateJsonSettings(bool filterCommandProperties)
    {
        var settings = new JsonSerializerSettings
        {
            ContractResolver = new CommandSerializerContractResolver(filterCommandProperties),
            Formatting = Formatting.None
        };

        settings.Converters.Add(new StringEnumConverter());
        settings.Converters.Add(new EntityIdConverter());
        settings.Converters.Add(new ResourceKeyConverter());

        return settings;
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
        string logEntry = JsonConvert.SerializeObject(environmentInfo, _unfilteredSettings);
        _writer.WriteLine(logEntry);

        // Start listening for executing commands
        _messengerService.Register<ExecutedCommandMessage>(this, OnExecutedCommandMessage);

        return Result.Ok();
    }

    private void OnExecutedCommandMessage(object recipient, ExecutedCommandMessage message)
    {
        var command = message.Command;

        var commandLogItem = new CommandItem(command.GetType().Name, message.ElapsedTime, message.ExecutionMode, command);

        string unfiltered = JsonConvert.SerializeObject(commandLogItem, _unfilteredSettings);
        string filtered = JsonConvert.SerializeObject(commandLogItem, _filteredSettings);

        Guard.IsNotNull(_writer);
        _writer.WriteLineAsync(unfiltered);
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
