using Celbridge.BaseLibrary.Console;
using Celbridge.BaseLibrary.Scripting;
using Celbridge.Console.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Celbridge.Console.ViewModels;

public partial class ConsoleViewModel : ObservableObject
{
    private readonly IConsoleService _consoleService;
    private readonly ICommandHistory _commandHistory;
    private IScriptContext? _scriptContext;

    [ObservableProperty]
    private string _commandText = string.Empty;

    public string Title => "Console";

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

    public ConsoleViewModel(
        IConsoleService consoleService,
        IScriptingService scriptingService)
    {
        _consoleService = consoleService;

        async Task InitScriptContext()
        {
            _scriptContext = await scriptingService.CreateScriptContext();
        }

        var _ = InitScriptContext();

        _commandHistory = _consoleService.CreateCommandHistory();

        _consoleService.OnPrint += Print;
    }

    public void ConsoleView_Unloaded()
    {
        _consoleService.OnPrint -= Print;
    }

    public ICommand ClearCommand => new RelayCommand(Clear_Executed);
    private void Clear_Executed()
    {
        _consoleLogItems.Clear();
    }

    public ICommand SubmitCommand => new AsyncRelayCommand(Submit_Executed);
    private async Task Submit_Executed()
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
               _commandHistory.AddCommand(command);
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

    public ICommand CloseCommand => new RelayCommand(CloseCommand_Executed);
    private void CloseCommand_Executed()
    {
        // Todo: Handle user request to close the console - e.g. push an undo operation to reopen the console
    }

    private void Print(MessageType messageType, string message)
    {
        _consoleLogItems.Add(new ConsoleLogItem(messageType, message, DateTime.Now));
    }
}
