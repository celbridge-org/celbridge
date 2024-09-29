using Celbridge.Commands;
using Celbridge.Console.Models;
using Celbridge.Messaging;
using Celbridge.Scripting;
using Celbridge.Utilities;
using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Localization;
using System.Collections.ObjectModel;

namespace Celbridge.Console.ViewModels;

public partial class ConsolePanelViewModel : ObservableObject
{
    private readonly IStringLocalizer _stringLocalizer;
    private readonly ICommandService _commandService;
    private readonly IConsoleService _consoleService;
    private readonly ICommandHistory _commandHistory;
    private IScriptContext? _scriptContext;

    [ObservableProperty]
    private string _commandText = string.Empty;

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
        IMessengerService messengerService,
        IServiceProvider serviceProvider,
        IStringLocalizer stringLocalizer,
        ICommandService commandService,
        IUtilityService utilityService,
        IConsoleService consoleService,
        IScriptingService scriptingService)
    {
        _stringLocalizer = stringLocalizer;
        _commandService = commandService;
        _consoleService = consoleService;

        async Task InitScriptContext()
        {
            _scriptContext = await scriptingService.CreateScriptContext();
        }

        var _ = InitScriptContext();

        _commandHistory = serviceProvider.GetRequiredService<ICommandHistory>();

        _consoleService.OnPrint += Print;

        var environmentInfo = utilityService.GetEnvironmentInfo();
        var versionString = stringLocalizer.GetString("ConsolePanel_ApplicationVersion", environmentInfo.AppVersion);
        Print(MessageType.Info, versionString);

        messengerService.Register<WorkspaceLoadedMessage>(this, (sender, message) =>
        {
            _ = _commandHistory.Load();
            messengerService.Unregister<WorkspaceLoadedMessage>(this);
        });
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
        _consoleService.OnPrint -= Print;
    }

    public ICommand ClearLogCommand => new RelayCommand(ClearLog_Executed);
    private void ClearLog_Executed()
    {
        _commandService.Execute<IClearLogCommand>();
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

        var createResult = _scriptContext.CreateExecutor(command);

        if (createResult.IsSuccess)
        {
            var executor = createResult.Value;
            Print(MessageType.Command, command);

            executor.OnOutput += (output) =>
            {
                Print(MessageType.Info, output);
            };

            executor.OnError += (error) =>
            {
                Print(MessageType.Error, error);
            };

            var executeResult = await executor.ExecuteAsync();
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
        }

        CommandText = string.Empty;
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

    private void Print(MessageType messageType, string message)
    {
        _consoleLogItems.Add(new ConsoleLogItem(messageType, message, DateTime.Now));
    }
}
