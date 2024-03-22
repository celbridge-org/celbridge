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

    public ICommand SelectFileCommand => new AsyncRelayCommand(SelectFile_Executed);
    private async Task SelectFile_Executed()
    {
        var result = await _dialogService.ShowFileOpenPicker();
        if (result.IsFailure)
        {
            _loggingService.Error(result.Error);
            return;
        }

        var path = result.Value;
        _loggingService.Info($"Path is : {path}");
    }
}
