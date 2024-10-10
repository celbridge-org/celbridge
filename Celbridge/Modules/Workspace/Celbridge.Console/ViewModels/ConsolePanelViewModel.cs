using Celbridge.Commands;
using Celbridge.Console.Models;
using Celbridge.Logging;
using Celbridge.Messaging;
using Celbridge.Scripting;
using Celbridge.Utilities;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Localization;
using Newtonsoft.Json;
using System.Collections.ObjectModel;

namespace Celbridge.Console.ViewModels;

public partial class ConsolePanelViewModel : ObservableObject
{
    private record LogEntry(string Level, string Message, LogEntryException? Exception);
    private record LogEntryException(string Type, string Message, string StackTrace);

    private readonly ILogger<ConsolePanelViewModel> _logger;
    private readonly IMessengerService _messengerService;
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IUtilityService _utilityService;
    private readonly IConsoleService _consoleService;
    private readonly IScriptingService _scriptingService;
    private readonly ICommandHistory _commandHistory;
    private IScriptContext? _scriptContext;

    [ObservableProperty]
    private string _commandText = string.Empty;

    public event Action? LogEntryAdded;

    private ObservableCollection<ConsoleLogItem> _consoleLogItems = new();
    public ObservableCollection<ConsoleLogItem> ConsoleLogItems
    {
        get => _consoleLogItems;
        set
        {
            _consoleLogItems = value;
            OnPropertyChanged(nameof(ConsoleLogItems));
        }
    }

    public ConsolePanelViewModel(
        ILogger<ConsolePanelViewModel> logger,
        IMessengerService messengerService,
        IServiceProvider serviceProvider,
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
        _scriptingService = workspaceWrapper.WorkspaceService.ScriptingService;

        _commandHistory = serviceProvider.GetRequiredService<ICommandHistory>();

        _consoleService.OnPrint += AppendLogEntry;

        messengerService.Register<LogEventMessage>(this, OnLogEventMessage);
    }

    public async Task<Result> InitializeScripting()
    {
        try
        {
            // Init the scripting context
            _scriptContext = await _scriptingService.CreateScriptContext();

            // Load previous command history
            await _commandHistory.Load();

            // Display the welcome message
            var environmentInfo = _utilityService.GetEnvironmentInfo();
            var welcomeString = _stringLocalizer.GetString("ConsolePanel_WelcomeMessage", environmentInfo.AppVersion);

            // .Net Interactive generates an error if the first script executed by the user is a #!import command.
            // As a workaround, we use scripting to log the welcome message, so subsequent #!import commands work fine.
            var script = $"Print(\"{welcomeString}\")";
            var executeResult = await Execute(script, false);
            if (executeResult.IsFailure)
            {
                return Result.Fail("Failed to print welcome message")
                    .AddErrors(executeResult);
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail(ex, "An exception occurred when initializing scripting");
        }
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
            return Result<LogEntry>.Fail(ex, "An exception occurred when parsing a log entry");
        }
    }

    public void ClearLog()
    {
        _consoleLogItems.Clear();
    }

    public void ClearHistory()
    {
        _commandHistory.Clear();
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

    public ICommand ExecuteCommand => new AsyncRelayCommand(ExecuteCommand_Executed);
    private async Task ExecuteCommand_Executed()
    {
        if (_scriptContext is null)
        {
            // Wait a short period to allow the context to initialize before we give up and throw an exception
            await Task.Delay(2000);
            if (_scriptContext is null)
            {
                throw new InvalidOperationException("The script context is not yet initialized.");
            }
        }

        // Remove leading and trailing whitespace from the entered text
        var command = CommandText.Trim();

        var executeResult = await Execute(CommandText, true);
        if (executeResult.IsSuccess)
        {
            // The ClearHistory() command should not be added to the history
            if (!command.StartsWith("ClearHistory()"))
            {
                _commandHistory.AddCommand(command);
            }

            // The command history persists between sessions.
            await _commandHistory.Save();
        }

        CommandText = string.Empty;
    }

    public async Task<Result> Execute(string command, bool logCommand)
    {
        Guard.IsNotNull(_scriptContext);

        // Trim the command again in case this method was call externally
        command = command.Trim();

        var createResult = _scriptContext.CreateExecutor(command);

        if (createResult.IsSuccess)
        {
            var executor = createResult.Value;

            if (logCommand)
            {
                // The command service will log detailed information about the command when it executes.
                // Here we just want to display a human readable version of the command, so we append it directly
                // rather than going via the logger.
                AppendLogEntry(MessageType.Command, command);
            }

            executor.OnOutput += (output) =>
            {
                _logger.LogInformation(output);
            };

            executor.OnError += (error) =>
            {
                _logger.LogError(error);
            };

            var executeResult = await executor.ExecuteAsync();
            if (executeResult.IsSuccess)
            {
                return Result.Ok();
            }
        }

        return createResult;
    }

    public ICommand SelectNextCommand => new RelayCommand(SelectNextCommand_Executed);
    private void SelectNextCommand_Executed()
    {
        if (_commandHistory.CanSelectNextCommand)
        {
            _commandHistory.SelectNextCommand();
            var result = _commandHistory.GetSelectedCommand();
            if (result.IsSuccess)
            {
                CommandText = result.Value;
            }
        }
    }

    public ICommand SelectPreviousCommand => new RelayCommand(SelectPreviousCommand_Executed);

    private void SelectPreviousCommand_Executed()
    {
        if (_commandHistory.CanSelectPreviousCommand)
        {
            _commandHistory.SelectPreviousCommand();
            var result = _commandHistory.GetSelectedCommand();
            if (result.IsSuccess)
            {
                CommandText = result.Value;
            }
        }
    }

    private void AppendLogEntry(MessageType messageType, string message)
    {
        _consoleLogItems.Add(new ConsoleLogItem(messageType, message, DateTime.Now));

        LogEntryAdded?.Invoke();
    }
}
