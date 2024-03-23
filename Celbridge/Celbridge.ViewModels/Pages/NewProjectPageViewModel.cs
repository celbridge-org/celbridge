using Celbridge.BaseLibrary.Logging;
using Celbridge.BaseLibrary.UserInterface;

namespace Celbridge.ViewModels.Pages;

public partial class NewProjectPageViewModel : ObservableObject
{
    private readonly ILoggingService _loggingService;
    private readonly IUserInterfaceService _userInterfaceService;

    public NewProjectPageViewModel(
        ILoggingService loggingService,
        IUserInterfaceService userInterfaceService)
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
}
