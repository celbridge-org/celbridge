using Celbridge.BaseLibrary.Logging;
using Celbridge.BaseLibrary.UserInterface.Navigation;
using Celbridge.BaseLibrary.UserInterface;
using Celbridge.BaseLibrary.Tasks;
using Celbridge.BaseLibrary.UserInterface.Dialog;

namespace Celbridge.ViewModels.Pages;

public partial class StartPageViewModel : ObservableObject
{
    private readonly ILoggingService _loggingService;
    private readonly IUserInterfaceService _userInterfaceService;

    public StartPageViewModel(
        INavigationService navigationService,
        ILoggingService loggingService,
        IUserInterfaceService userInterfaceService,
        ISchedulerService schedulerService)
    {
        _loggingService = loggingService;
        _userInterfaceService = userInterfaceService;
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
}

