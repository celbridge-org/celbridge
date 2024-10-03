using Celbridge.Dialog;
using Celbridge.FilePicker;
using Celbridge.Navigation;
using Celbridge.UserInterface.Services;

namespace Celbridge.UserInterface.ViewModels;

public partial class HomePageViewModel : ObservableObject
{
    private readonly Logging.ILogger<HomePageViewModel> _logger;
    private readonly IFilePickerService _filePickerService;
    private readonly IDialogService _dialogService;
    private readonly MainMenuUtils _mainMenuUtils;

    public HomePageViewModel(
        INavigationService navigationService,
        Logging.ILogger<HomePageViewModel> logger,
        IFilePickerService filePickerService,
        IDialogService dialogService,
        MainMenuUtils mainMenuUtils)
    {
        _logger = logger;
        _filePickerService = filePickerService;
        _dialogService = dialogService;
        _mainMenuUtils = mainMenuUtils;
    }

    public IAsyncRelayCommand NewProjectCommand => new AsyncRelayCommand(NewProjectCommand_Executed);
    private async Task NewProjectCommand_Executed()
    {
        await _mainMenuUtils.CreateProjectAsync();
    }

    public IAsyncRelayCommand OpenProjectCommand => new AsyncRelayCommand(OpenProjectCommand_Executed);
    private async Task OpenProjectCommand_Executed()
    {
        await _mainMenuUtils.OpenProjectAsync();
    }
}

