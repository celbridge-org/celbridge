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

    private ObservableCollection<string> _outputItems = new ();
    public ObservableCollection<string> OutputItems
    {
        get => _outputItems;
        set
        {
            _outputItems = value;
            OnPropertyChanged(nameof(OutputItems));
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
        _outputItems.Clear();
    }

    public ICommand SubmitCommand => new RelayCommand(Submit_Executed);
    private void Submit_Executed()
    {
        _outputItems.Add(CommandText);
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
