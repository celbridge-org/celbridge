using Celbridge.BaseLibrary.Console;
using Celbridge.BaseLibrary.UserInterface;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Celbridge.Console.ViewModels;

public partial class ConsoleViewModel : ObservableObject
{
    private readonly IConsoleService _consoleService;

    private readonly ICommandHistory _commandHistory;

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
        IUserInterfaceService userInterfaceService, 
        IConsoleService consoleService)
    {
        _consoleService = consoleService;

        _commandHistory = _consoleService.CreateCommandHistory();
    }

    public ICommand ClearCommand => new RelayCommand(Clear_Executed);
    private void Clear_Executed()
    {
        _consoleLogItems.Clear();
    }

    public ICommand SubmitCommand => new RelayCommand(Submit_Executed);
    private void Submit_Executed()
    {
        // Remove leading and trailing whitespace from the entered text
        var command = CommandText.Trim();

        _consoleLogItems.Add(new ConsoleLogItem(ConsoleLogType.Command, command, DateTime.Now));
        _consoleLogItems.Add(new ConsoleLogItem(ConsoleLogType.Info, command, DateTime.Now));

        _commandHistory.AddCommand(command);

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
