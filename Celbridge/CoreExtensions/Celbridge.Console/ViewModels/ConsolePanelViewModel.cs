using Celbridge.BaseLibrary.Console;
using Celbridge.BaseLibrary.UserInterface;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Console.ViewModels;

public partial class ConsolePanelViewModel : ObservableObject
{
    private readonly IConsoleService _consoleService;

    public event Action? OnClearConsole;
    public event Action? OnAddConsoleTab;

    public ConsolePanelViewModel(
        IUserInterfaceService userInterfaceService, 
        IConsoleService consoleService)
    {
        _consoleService = consoleService;

        // Register the singleton console service with the workspace service
        userInterfaceService.WorkspaceService.RegisterService(_consoleService);
    }

    public ICommand ClearCommand => new RelayCommand(ClearCommand_Executed);
    private void ClearCommand_Executed()
    {
        OnClearConsole?.Invoke();
    }

    public ICommand AddConsoleTabCommand => new RelayCommand(AddConsoleTabCommand_Executed);
    private void AddConsoleTabCommand_Executed()
    {
        OnAddConsoleTab?.Invoke();
    }
}
