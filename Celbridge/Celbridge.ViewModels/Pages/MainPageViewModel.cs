using Celbridge.BaseLibrary.Commands.Project;
using Celbridge.BaseLibrary.Commands;
using Celbridge.BaseLibrary.Dialog;
using Celbridge.BaseLibrary.FilePicker;
using Celbridge.BaseLibrary.Logging;
using Celbridge.BaseLibrary.Navigation;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.UserInterface;
using Celbridge.BaseLibrary.Workspace;
using Celbridge.Services.Navigation;
using CommunityToolkit.Diagnostics;

namespace Celbridge.ViewModels.Pages;

public partial class MainPageViewModel : ObservableObject, INavigationProvider
{
    public const string StartTag = "Start";
    public const string NewProjectTag = "NewProject";
    public const string OpenProjectTag = "OpenProject";
    public const string CloseProjectTag = "CloseProject";
    public const string SettingsTag = "Settings";

    public const string StartPageName = "StartPage";

    private readonly IMessengerService _messengerService;
    private readonly ILoggingService _loggingService;
    private readonly INavigationService _navigationService;
    private readonly IProjectDataService _projectDataService;
    private readonly IDialogService _dialogService;
    private readonly IFilePickerService _filePickerService;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly ICommandService _commandService;

    public MainPageViewModel(
        IMessengerService messengerService,
        ILoggingService loggingService, 
        INavigationService navigationService,
        IProjectDataService projectDataService,
        IDialogService dialogService,
        IFilePickerService filePickerService,
        IWorkspaceWrapper workspaceWrapper,
        ICommandService commandService)
    {
        _messengerService = messengerService;
        _loggingService = loggingService;
        _navigationService = navigationService;
        _projectDataService = projectDataService;
        _dialogService = dialogService;
        _filePickerService = filePickerService;
        _workspaceWrapper = workspaceWrapper;
        _commandService = commandService;
    }

    public bool IsProjectDataLoaded => _projectDataService.LoadedProjectData is not null;

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
            OnPropertyChanged(nameof(IsProjectDataLoaded)); 
        });

        _messengerService.Register<WorkspaceUnloadedMessage>(this, (r, m) =>
        {
            OnPropertyChanged(nameof(IsProjectDataLoaded));
        });

        // Register this class as the navigation provider for the application
        var navigationService = _navigationService as NavigationService;
        Guard.IsNotNull(navigationService);
        navigationService.SetNavigationProvider(this);

        // Navigate to the start page at startup
        _navigationService.NavigateToPage(StartPageName);

        // Start executing queued commands
        _commandService.StartExecutingCommands();

        // Todo: Add a user setting to automatically open the previously loaded project.
    }

    public void OnMainPage_Unloaded()
    {}

    public void SelectNavigationItem(string tag)
    {
        if (tag == NewProjectTag)
        {
            _ = CreateProjectAsync();
            return;
        }

        if (tag == OpenProjectTag)
        {
            _ = OpenProjectAsync();
            return;
        }

        if (tag == CloseProjectTag)
        {
            _ = CloseProjectAsync();
            return;
        }

        var navigationResult = _navigationService.NavigateToPage(tag);
        if (navigationResult.IsSuccess)
        {
            return;
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

    private async Task CloseProjectAsync()
    {
        if (!IsProjectDataLoaded && !_workspaceWrapper.IsWorkspaceLoaded)
        {
            // No project loaded so nothing to do here.
            return;
        }

        var command = _commandService.CreateCommand<IUnloadProjectCommand>();
        _commandService.EnqueueCommand(command);

        // Wait until the project is unloaded before navigating
        while (IsProjectDataLoaded)
        {
            await Task.Delay(50);
        }

        _navigationService.NavigateToPage(StartPageName);
    }
}

