using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Utilities;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Localization;
using Newtonsoft.Json;

namespace Celbridge.Console.ViewModels;

public partial class ConsolePanelViewModel : ObservableObject
{
    private readonly ILogger<ConsolePanelViewModel> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IUtilityService _utilityService;
    private readonly IConsoleService _consoleService;

    private record LogEntry(string Level, string Message, LogEntryException? Exception);
    private record LogEntryException(string Type, string Message, string StackTrace);

    public event Action? LogEntryAdded;

    public ConsolePanelViewModel(
        IServiceProvider serviceProvider,
        ILogger<ConsolePanelViewModel> logger,
        IMessengerService messengerService,
        IStringLocalizer stringLocalizer,
        ICommandService commandService,
        IUtilityService utilityService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _messengerService = messengerService;
        _stringLocalizer = stringLocalizer;
        _commandService = commandService;
        _utilityService = utilityService;

        _consoleService = workspaceWrapper.WorkspaceService.ConsoleService;

        _consoleService.OnPrint += AppendLogEntry;

        messengerService.Register<LogEventMessage>(this, OnLogEventMessage);
    }

    private void OnLogEventMessage(object recipient, LogEventMessage message)
    {
        var json = message.LogEventJson;
        var parseResult = ParseLogEntry(json);
        if (parseResult.IsFailure) 
        {
            // An error occurred parsing the message json
            AppendLogEntry(MessageType.Error, parseResult.Error);
            return;
        }

        var logEntry = parseResult.Value;

        switch (logEntry.Level)
        {
            case "Trace":
            case "Debug":
                // Don't print this log level
                break;
            case "Info":
                AppendLogEntry(MessageType.Information, logEntry.Message);
                break;
            case "Warn":
                AppendLogEntry(MessageType.Warning, logEntry.Message);
                break;
            case "Error":
            case "Fatal":
                if (logEntry.Exception is null)
                {
                   AppendLogEntry(MessageType.Error, logEntry.Message);
                }
                else
                {
                    var logText = $"{logEntry.Message}{Environment.NewLine}{logEntry.Exception.Type}: {logEntry.Exception.Message}";
                    AppendLogEntry(MessageType.Error, logText);
                }

                break;
        }
    }

    private Result<LogEntry> ParseLogEntry(string json)
    {
        try
        {
            var logEntry = JsonConvert.DeserializeObject<LogEntry>(json);
            if (logEntry is null)
            {
                return Result<LogEntry>.Fail("Failed to deserialize log entry");
            }

            return Result<LogEntry>.Ok(logEntry);
        }
        catch (Exception ex) 
        {
            return Result<LogEntry>.Fail("An exception occurred when parsing a log entry")
                .WithException(ex);
        }
    }

    public void ConsoleView_Unloaded()
    {
        _consoleService.OnPrint -= AppendLogEntry;
    }

    public ICommand ClearLogCommand => new RelayCommand(ClearLog_Executed);
    private void ClearLog_Executed()
    {
        _commandService.Execute<IClearCommand>();
    }


    private void AppendLogEntry(MessageType messageType, string message)
    {
        LogEntryAdded?.Invoke();
    }
}
