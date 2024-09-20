using Celbridge.Dialog;
using Celbridge.FilePicker;
using Celbridge.Navigation;

namespace Celbridge.UserInterface.ViewModels;

public partial class HomePageViewModel : ObservableObject
{
    private readonly Logging.ILogger<HomePageViewModel> _logger;
    private readonly IFilePickerService _filePickerService;
    private readonly IDialogService _dialogService;

    public HomePageViewModel(
        INavigationService navigationService,
        Logging.ILogger<HomePageViewModel> logger,
        IFilePickerService filePickerService,
        IDialogService dialogService)
    {
        _logger = logger;
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
            _logger.LogError(result.Error);
            return;
        }

        var path = result.Value;
        _logger.LogInformation($"Selected path is : {path}");
    }

    public ICommand SelectFolderCommand => new AsyncRelayCommand(SelectFolder_ExecutedAsync);
    private async Task SelectFolder_ExecutedAsync()
    {
        var result = await _filePickerService.PickSingleFolderAsync();
        if (result.IsFailure)
        {
            _logger.LogError(result.Error);
            return;
        }

        var path = result.Value;
        _logger.LogInformation($"Selected path is : {path}");
    }

    public ICommand ShowAlertDialogCommand => new AsyncRelayCommand(ShowAlertDialog_ExecutedAsync);
    private async Task ShowAlertDialog_ExecutedAsync()
    {
        await _dialogService.ShowAlertDialogAsync("Some title", "Some message");
    }

    public ICommand ShowProgressDialogCommand => new RelayCommand(ShowProgressDialog_Executed);
    private void ShowProgressDialog_Executed()
    {
        _dialogService.AcquireProgressDialog("Some title");
    }
}

