using Celbridge.BaseLibrary.Logging;
using Celbridge.BaseLibrary.UserInterface.Navigation;
using Celbridge.BaseLibrary.UserInterface;
using Celbridge.BaseLibrary.Tasks;
using Celbridge.BaseLibrary.UserInterface.Dialog;

namespace Celbridge.ViewModels.Pages;

public partial class StartPageViewModel : ObservableObject
{
    private readonly string WorkspacePageName = "WorkspacePage";
    private readonly string ShellName = "Shell";

    private readonly ILoggingService _loggingService;
    private readonly INavigationService _navigationService;
    private readonly IUserInterfaceService _userInterfaceService;
    private readonly ISchedulerService _schedulerService;

    public StartPageViewModel(
        INavigationService navigationService,
        ILoggingService loggingService,
        IUserInterfaceService userInterfaceService,
        ISchedulerService schedulerService)
    {
        _navigationService = navigationService;
        _loggingService = loggingService;
        _userInterfaceService = userInterfaceService;
        _schedulerService = schedulerService;
    }

    public ICommand OpenWorkspacePageCommand => new RelayCommand(OpenWorkspacePageCommand_Executed);
    private void OpenWorkspacePageCommand_Executed()
    {
        _navigationService.NavigateToPage(WorkspacePageName);
    }

    public ICommand LegacyInterfaceCommand => new RelayCommand(LegacyInterfaceCommand_Executed);
    private void LegacyInterfaceCommand_Executed()
    {
        _navigationService.NavigateToPage(ShellName);
    }

    public ICommand SelectFileCommand => new AsyncRelayCommand(SelectFile_ExecutedAsync);
    private async Task SelectFile_ExecutedAsync()
    {
        var extensions = new List<string>()
        {
            ".txt"
        };

        var filePickerService = _userInterfaceService.FilePickerService;

        var result = await filePickerService.PickSingleFileAsync(extensions);
        if (result.IsFailure)
        {
            _loggingService.Error(result.Error);
            return;
        }

        var path = result.Value;
        _loggingService.Info($"Path is : {path}");
    }

    public ICommand SelectFolderCommand => new AsyncRelayCommand(SelectFolder_ExecutedAsync);
    private async Task SelectFolder_ExecutedAsync()
    {
        var filePickerService = _userInterfaceService.FilePickerService;

        var result = await filePickerService.PickSingleFolderAsync();
        if (result.IsFailure)
        {
            _loggingService.Error(result.Error);
            return;
        }

        var path = result.Value;
        _loggingService.Info($"Path is : {path}");
    }

    public ICommand ShowAlertDialogCommand => new AsyncRelayCommand(ShowAlertDialog_ExecutedAsync);
    private async Task ShowAlertDialog_ExecutedAsync()
    {
        var dialogService = _userInterfaceService.DialogService;

        await dialogService.ShowAlertDialogAsync("Some title", "Some message", "Ok");
    }

    public ICommand ShowProgressDialogCommand => new RelayCommand(ShowProgressDialog_Executed);
    private void ShowProgressDialog_Executed()
    {
        var dialogService = _userInterfaceService.DialogService;

        dialogService.AcquireProgressDialog("Some title");
    }

    public ICommand ScheduleTaskCommand => new RelayCommand(ScheduleTask_Executed);
    private void ScheduleTask_Executed()
    {
        var dialogService = _userInterfaceService.DialogService;

        IProgressDialogToken? token;

        _schedulerService.ScheduleFunction(async () =>
        {
            token = dialogService.AcquireProgressDialog("Doing stuff");
            await Task.Delay(1000);            
            // Displaying this alert automatically suppresses the progress dialog until the alert is dismissed
            await dialogService.ShowAlertDialogAsync("Scheduled Alert", "An alert message", "Ok");
            await Task.Delay(1000);
            dialogService.ReleaseProgressDialog(token);
        });
    }
}

