using Celbridge.BaseLibrary.Console;
using Celbridge.BaseLibrary.UserInterface;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Celbridge.Console.ViewModels;

public partial class ConsolePanelViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConsoleService _consoleService;

    public ObservableCollection<ConsoleTabItemViewModel> ConsoleTabs { get; } = new();

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
    {}

    public ICommand AddConsoleTabCommand => new RelayCommand(AddConsoleTabCommand_Executed);
    private void AddConsoleTabCommand_Executed()
    {
        var consoleTabItem = _serviceProvider.GetRequiredService<ConsoleTabItemViewModel>();
        ConsoleTabs.Add(consoleTabItem);
    }

    public ICommand CloseConsoleTabCommand => new RelayCommand(CloseConsoleTabCommand_Executed);
    private void CloseConsoleTabCommand_Executed()
    {
        // Todo: Remove the console tab from the collection
        // I think the tab itself has to request this?
    }

    public override string ToString()
    {
        return "Console";
    }
}
