using Celbridge.Messaging;
using Celbridge.Projects;
using Celbridge.Utilities;
using CommunityToolkit.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Celbridge.Commands.Services;

public class CommandLogger : ICommandLogger, IDisposable
{
    private const string LogFilePrefix = "CommandLog";
    private const int MaxFilesToKeep = 0;

    private record CommandLogItem(string CommandName, float ElapsedTime, CommandExecutionMode ExecutionMode, IExecutableCommand Command);

    private readonly IMessengerService _messengerService;
    private readonly IUtilityService _utilityService;
    private readonly IProjectDataService _projectDataService;

    private readonly JsonSerializerSettings _jsonSerializerSettings;

    private StreamWriter? _writer;

    public CommandLogger(
        IMessengerService messengerService,
        IUtilityService utilityService,
        IProjectDataService projectDataService)
    {
        _messengerService = messengerService;
        _utilityService = utilityService;
        _projectDataService = projectDataService;

        // var ignoreProperties = new[] { "CommandId", "UndoGroupId", "UndoStackName" };
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

    public Result StartLogging()
    {
        var loadedProjectData = _projectDataService.LoadedProjectData;
        if (loadedProjectData is null)
        {
            return Result.Fail("No project data loaded.");
        }

        // Acquire the log file path

        var timestamp = _utilityService.GetTimestamp();
        string logFolderPath = loadedProjectData.LogFolderPath;
        string logFilename = $"{LogFilePrefix}_{timestamp}.jsonl";
        string logFilePath = Path.Combine(logFolderPath, logFilename);

        var logFolder = Path.GetDirectoryName(logFilePath)!;
        if (!Directory.Exists(logFolder))
        {
            Directory.CreateDirectory(logFolder);
        }

        // Delete old log files

        var deleteResult = _utilityService.DeleteOldFiles(logFolderPath, LogFilePrefix, MaxFilesToKeep);
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
