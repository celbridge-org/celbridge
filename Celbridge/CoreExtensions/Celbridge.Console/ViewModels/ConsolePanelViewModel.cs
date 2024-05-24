using Celbridge.BaseLibrary.Console;
using Celbridge.BaseLibrary.Tasks;
using Celbridge.BaseLibrary.UserInterface;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Console.ViewModels;

public partial class ConsolePanelViewModel : ObservableObject
{
    private readonly IConsoleService _consoleService;
    private readonly IUserInterfaceService _userInterfaceService;

    public event Action? OnClearConsole;
    public event Action? OnAddConsoleTab;

    public ConsolePanelViewModel(
        IUserInterfaceService userInterfaceService, 
        IConsoleService consoleService)
    {
        _consoleService = consoleService;
        _userInterfaceService = userInterfaceService;

        // Register the singleton console service with the workspace service
        userInterfaceService.WorkspaceService.RegisterService(_consoleService);
    }

    public ICommand ClearCommand => new AsyncRelayCommand(ClearCommand_Executed);
    private async Task ClearCommand_Executed()
    {
        var schedulerService = ServiceLocator.ServiceProvider.GetRequiredService<ISchedulerService>();

        schedulerService.ScheduleFunction(async () =>
        {
            var userInterfaceService = ServiceLocator.ServiceProvider.GetRequiredService<IUserInterfaceService>();
            var dialogService = userInterfaceService.DialogService;

            await dialogService.ShowAlertDialogAsync("1", "2", "Ok");
            //await dialogService.ShowAlertDialogAsync(title, message, "Ok");
        });

        //var dialogService = _userInterfaceService.DialogService;
        //await dialogService.ShowAlertDialogAsync("Some title", "Some message", "Ok");
    }

    public ICommand AddConsoleTabCommand => new RelayCommand(AddConsoleTabCommand_Executed);
    private void AddConsoleTabCommand_Executed()
    {
        OnAddConsoleTab?.Invoke();
    }
}
