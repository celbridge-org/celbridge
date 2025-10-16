using Celbridge.Commands;
using Celbridge.Logging;
using Celbridge.Utilities;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Localization;
using Newtonsoft.Json;

namespace Celbridge.Console.ViewModels;

public partial class ConsolePanelViewModel : ObservableObject
{
    private readonly ILogger<ConsolePanelViewModel> _logger;
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
        IStringLocalizer stringLocalizer,
        ICommandService commandService,
        IUtilityService utilityService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _stringLocalizer = stringLocalizer;
        _commandService = commandService;
        _utilityService = utilityService;

        _consoleService = workspaceWrapper.WorkspaceService.ConsoleService;
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

    public ICommand ClearLogCommand => new RelayCommand(ClearLog_Executed);
    private void ClearLog_Executed()
    {
        _commandService.Execute<IClearCommand>();
    }
}
