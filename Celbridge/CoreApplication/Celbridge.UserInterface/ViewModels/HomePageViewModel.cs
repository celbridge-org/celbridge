using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.FilePicker;
using Celbridge.BaseLibrary.Logging;
using Celbridge.BaseLibrary.Navigation;

namespace Celbridge.UserInterface.ViewModels;

public partial class HomePageViewModel : ObservableObject
{
    private readonly ILoggingService _loggingService;
    private readonly IFilePickerService _filePickerService;
    private readonly IDialogService _dialogService;

    public HomePageViewModel(
        INavigationService navigationService,
        ILoggingService loggingService,
        IFilePickerService filePickerService,
        IDialogService dialogService)
    {
        _loggingService = loggingService;
        _filePickerService = filePickerService;
        _dialogService = dialogService;
    }

    public ICommand SelectFileCommand => new AsyncRelayCommand(SelectFile_ExecutedAsync);
    private async Task SelectFile_ExecutedAsync()
    {
        var extensions = new List<string>()
        {
            ".txt"
        };

        var result = await _filePickerService.PickSingleFileAsync(extensions);
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
        var result = await _filePickerService.PickSingleFolderAsync();
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
        await _dialogService.ShowAlertDialogAsync("Some title", "Some message", "Ok");
    }

    public ICommand ShowProgressDialogCommand => new RelayCommand(ShowProgressDialog_Executed);
    private void ShowProgressDialog_Executed()
    {
        _dialogService.AcquireProgressDialog("Some title");
    }
}

