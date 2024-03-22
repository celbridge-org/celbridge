using Celbridge.BaseLibrary.Dialogs;
using Celbridge.BaseLibrary.Logging;

namespace Celbridge.ViewModels.Pages;

public partial class NewProjectPageViewModel : ObservableObject
{
    private readonly ILoggingService _loggingService;
    private readonly IDialogService _dialogService;

    public NewProjectPageViewModel(
        ILoggingService loggingService,
        IDialogService dialogService)
    {
        _loggingService = loggingService;
        _dialogService = dialogService;
    }

    public ICommand SelectFileCommand => new AsyncRelayCommand(SelectFile_ExecutedAsync);
    private async Task SelectFile_ExecutedAsync()
    {
        var extensions = new List<string>()
        {
            ".txt"
        };
        var result = await _dialogService.PickSingleFileAsync(extensions);
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
        var result = await _dialogService.PickSingleFolderAsync();
        if (result.IsFailure)
        {
            _loggingService.Error(result.Error);
            return;
        }

        var path = result.Value;
        _loggingService.Info($"Path is : {path}");
    }    
}
