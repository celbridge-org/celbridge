using Celbridge.BaseLibrary.Console;
using Celbridge.BaseLibrary.Scripting;
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
            // Todo: Wait for a while to allow the context to finish initializing, but fail if it takes too long.
            return;
        }

        // Remove leading and trailing whitespace from the entered text
        var command = CommandText.Trim();

        var createResult = _scriptContext.CreateExecutor(command);

        if (createResult.IsSuccess)
        {
            var executor = createResult.Value;
            _consoleLogItems.Add(new ConsoleLogItem(ConsoleLogType.Command, command, DateTime.Now));

            executor.OnOutput += (output) =>
            {
                _consoleLogItems.Add(new ConsoleLogItem(ConsoleLogType.Info, output, DateTime.Now));
            };

            executor.OnError += (error) =>
            {
                _consoleLogItems.Add(new ConsoleLogItem(ConsoleLogType.Error, error, DateTime.Now));
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
}
