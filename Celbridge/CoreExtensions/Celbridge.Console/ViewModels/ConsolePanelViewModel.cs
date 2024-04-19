using Celbridge.BaseLibrary.Console;
using Celbridge.BaseLibrary.UserInterface;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Celbridge.Console.ViewModels;

public partial class ConsolePanelViewModel : ObservableObject
{
    private readonly IConsoleService _consoleService;

    private readonly ICommandHistory _commandHistory;

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
        IUserInterfaceService userInterfaceService, 
        IConsoleService consoleService)
    {
        _consoleService = consoleService;  // Transient instance created via DI

        // Register the console service with the workspace service
        userInterfaceService.WorkspaceService.RegisterService(_consoleService);

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
        _consoleLogItems.Add(new ConsoleLogItem(ConsoleLogType.Command, CommandText, DateTime.Now));

        // _consoleLogItems.Add(new ConsoleLogItem(ConsoleLogType.Info, CommandText, DateTime.Now));

        _commandHistory.AddCommand(CommandText);

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
}
