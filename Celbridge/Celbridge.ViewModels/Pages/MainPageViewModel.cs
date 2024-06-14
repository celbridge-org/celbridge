using Celbridge.BaseLibrary.Commands.Project;
using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.FilePicker;
using Celbridge.BaseLibrary.Logging;
using Celbridge.BaseLibrary.Navigation;
using Celbridge.BaseLibrary.Workspace;
using Celbridge.Services.Navigation;
using CommunityToolkit.Diagnostics;

namespace Celbridge.ViewModels.Pages;

public partial class MainPageViewModel : ObservableObject, INavigationProvider
{
    public const string HomeTag = "Home";
    public const string NewProjectTag = "NewProject";
    public const string OpenProjectTag = "OpenProject";
    public const string SettingsTag = "Settings";
    public const string LegacyTag = "Legacy";

    private const string StartPageName = "StartPage";
    private const string SettingsPageName = "SettingsPage";
    private const string LegacyPageName = "Shell";

    private readonly IMessengerService _messengerService;
    private readonly ILoggingService _loggingService;
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;
    private readonly IFilePickerService _filePickerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ICommandService _commandService;

    public MainPageViewModel(
        IMessengerService messengerService,
        ILoggingService loggingService, 
        INavigationService navigationService,
        IDialogService dialogService,
        IFilePickerService filePickerService,
        IWorkspaceWrapper workspaceWrapper,
        ICommandService commandService)
    {
        _messengerService = messengerService;
        _loggingService = loggingService;
        _navigationService = navigationService;
        _dialogService = dialogService;
        _filePickerService = filePickerService;
        _workspaceWrapper = workspaceWrapper;
        _commandService = commandService;
    }

    public bool IsWorkspaceLoaded => _workspaceWrapper.IsWorkspaceLoaded;

    public event Func<Type, object, Result>? OnNavigate;

    public Result NavigateToPage(Type pageType)
    {
        // Pass the empty string to avoid making the parameter nullable.
        return NavigateToPage(pageType, string.Empty);
    }

    public Result NavigateToPage(Type pageType, object parameter)
    {
        return OnNavigate?.Invoke(pageType, parameter)!;
    }

    public void OnMainPage_Loaded()
    {
        _messengerService.Register<WorkspaceLoadedMessage>(this, (r,m) => 
        { 
            OnPropertyChanged(nameof(IsWorkspaceLoaded)); 
        });

        _messengerService.Register<WorkspaceUnloadedMessage>(this, (r, m) =>
        {
            OnPropertyChanged(nameof(IsWorkspaceLoaded));
        });

        // Register this class as the navigation provider for the application
        var navigationService = _navigationService as NavigationService;
        Guard.IsNotNull(navigationService);
        navigationService.SetNavigationProvider(this);

        // Todo: Add a user setting to automatically open the previously loaded project instead of the start page

        // Navigate to the start page at startup
        _navigationService.NavigateToPage(StartPageName);
    }

    public void OnMainPage_Unloaded()
    {}

    public void SelectNavigationItem(string tag)
    {
        switch (tag)
        {
            case HomeTag:
                _ = NavigateToHomeAsync();
                return;

            case NewProjectTag:
                _ = CreateProjectAsync();
                return;

            case OpenProjectTag:
                _ = OpenProjectAsync();
                return;

            case SettingsTag:
                _navigationService.NavigateToPage(SettingsPageName);
                break;

            case LegacyTag:
                _navigationService.NavigateToPage(LegacyPageName);
                break;
        }

        _loggingService.Error($"Failed to navigate to item {tag}.");
    }

    private async Task CreateProjectAsync()
    {
        var showResult = await _dialogService.ShowNewProjectDialogAsync();
        if (showResult.IsSuccess)
        {
            var projectConfig = showResult.Value;

            var command = _commandService.CreateCommand<ICreateProjectCommand>();
            command.Config = projectConfig;
            _commandService.EnqueueCommand(command);
        }
    }

    private async Task OpenProjectAsync()
    {
        var result = await _filePickerService.PickSingleFileAsync(new List<string> { ".celbridge" });
        if (result.IsSuccess)
        {
            var projectPath = result.Value;

            var command = _commandService.CreateCommand<ILoadProjectCommand>();
            command.ProjectPath = projectPath;
            _commandService.EnqueueCommand(command);
        }
    }

    private async Task NavigateToHomeAsync()
    {
        if (IsWorkspaceLoaded)
        {
            var command = _commandService.CreateCommand<IUnloadProjectCommand>();
            _commandService.EnqueueCommand(command);

            // Wait until the project is unloaded before navigating
            while (IsWorkspaceLoaded)
            {
                await Task.Delay(50);
            }
        }

        _navigationService.NavigateToPage(StartPageName);
    }
}

