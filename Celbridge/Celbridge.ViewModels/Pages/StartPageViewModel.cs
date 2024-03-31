using Celbridge.BaseLibrary.Logging;
using Celbridge.BaseLibrary.UserInterface.Navigation;
using Celbridge.BaseLibrary.UserInterface;

namespace Celbridge.ViewModels.Pages;

public partial class StartPageViewModel : ObservableObject
{
    private readonly string WorkspacePageName = "WorkspacePage";
    private readonly string ShellName = "Shell";

    private readonly ILoggingService _loggingService;
    private readonly INavigationService _navigationService;
    private readonly IUserInterfaceService _userInterfaceService;

    public StartPageViewModel(
        INavigationService navigationService,
        ILoggingService loggingService,
        IUserInterfaceService userInterfaceService)
    {
        _navigationService = navigationService;
        _loggingService = loggingService;
        _userInterfaceService = userInterfaceService;
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

    public ICommand ShowProgressDialogCommand => new AsyncRelayCommand(ShowProgressDialog_ExecutedAsync);
    private async Task ShowProgressDialog_ExecutedAsync()
    {
        var dialogService = _userInterfaceService.DialogService;

        await dialogService.ShowProgressDialogAsync("Some title", "Cancel");
    }
}

