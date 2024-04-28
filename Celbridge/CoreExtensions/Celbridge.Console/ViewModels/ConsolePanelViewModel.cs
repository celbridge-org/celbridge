using Celbridge.BaseLibrary.Console;
using Celbridge.BaseLibrary.UserInterface;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Console.ViewModels;

public partial class ConsolePanelViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConsoleService _consoleService;

    public event Action? OnClearConsole;
    public event Action? OnAddConsoleTab;

    public ConsolePanelViewModel(
        IServiceProvider serviceProvider,
        IUserInterfaceService userInterfaceService, 
        IConsoleService consoleService)
    {
        _serviceProvider = serviceProvider;
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
