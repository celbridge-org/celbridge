using Celbridge.BaseLibrary.Logging;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Tasks;
using Celbridge.BaseLibrary.UserInterface;
using Celbridge.BaseLibrary.UserInterface.Navigation;
using Celbridge.BaseLibrary.Workspace;
using Celbridge.Services.UserInterface.Navigation;
using CommunityToolkit.Diagnostics;

namespace Celbridge.ViewModels.Pages;

public partial class MainPageViewModel : ObservableObject, INavigationProvider
{
    public const string StartTag = "Start";
    public const string NewProjectTag = "NewProject";
    public const string OpenProjectTag = "OpenProject";
    public const string CloseProjectTag = "CloseProject";
    public const string SettingsTag = "Settings";

    public const string EmptyPageName = "EmptyPage";
    public const string StartPageName = "StartPage";
    public const string WorkspacePageName = "WorkspacePage";

    private readonly IMessengerService _messengerService;
    private readonly ILoggingService _loggingService;
    private readonly INavigationService _navigationService;
    private readonly IProjectDataService _projectDataService;
    private readonly IUserInterfaceService _userInterfaceService;
    private readonly ISchedulerService _schedulerService;

    public MainPageViewModel(
        IMessengerService messengerService,
        ILoggingService loggingService, 
        INavigationService navigationService,
        IProjectDataService projectDataService,
        IUserInterfaceService userInterfaceService,
        ISchedulerService schedulerService)
    {
        _messengerService = messengerService;
        _loggingService = loggingService;
        _navigationService = navigationService;
        _projectDataService = projectDataService;
        _userInterfaceService = userInterfaceService;
        _schedulerService = schedulerService;
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
            async Task CloseAsync()
            {
                await CloseProjectAsync();
                _navigationService.NavigateToPage(StartPageName);
            }
            _ = CloseAsync();
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
        var userInterfaceService = ServiceLocator.ServiceProvider.GetRequiredService<IUserInterfaceService>();
        var dialogService = userInterfaceService.DialogService;

        // The new project dialog takes care of creating the project folder and the project data file.

        var showResult = await dialogService.ShowNewProjectDialogAsync();
        if (showResult.IsSuccess)
        {
            var projectPath = showResult.Value;
            var projectDataService = _projectDataService; // Avoid capturing "this"

            await CloseProjectAsync();

            var openResult = projectDataService.OpenProjectData(projectPath);
            if (openResult.IsFailure)
            {
                _loggingService.Error($"Failed to open project: {openResult.Error}");
                return;
            }

            _navigationService.NavigateToPage(WorkspacePageName);
        }
    }

    private async Task OpenProjectAsync()
    {
        var userInterfaceService = ServiceLocator.ServiceProvider.GetRequiredService<IUserInterfaceService>();
        var filePickerService = userInterfaceService.FilePickerService;
        var result = await filePickerService.PickSingleFileAsync(new List<string> { ".celbridge" });
        if (result.IsSuccess)
        {
            var projectPath = result.Value;
            var projectDataService = _projectDataService; // Avoid capturing "this"

            if (_projectDataService.LoadedProjectData?.ProjectPath == projectPath)
            {
                // The project is already loaded.
                // We can just early out here as we're already in the expected end state.
                return;
            }

            await CloseProjectAsync();

            var openResult = projectDataService.OpenProjectData(projectPath);
            if (openResult.IsFailure)
            {
                _loggingService.Error($"Failed to open project: {openResult.Error}");
                return;
            }

            _navigationService.NavigateToPage(WorkspacePageName);
        }
    }

    private async Task CloseProjectAsync()
    {
        if (!IsProjectDataLoaded && !_userInterfaceService.IsWorkspaceLoaded)
        {
            // No project loaded so nothing to do here.
            return;
        }

        var projectDataService = _projectDataService; // Avoid capturing "this"

        // Todo: Notify the workspace that it is about to close.
        // The workspace may want to schedule some operations (e.g. save changes) before we close the project workspace.

        _schedulerService.ScheduleFunction(async () =>
        {
            projectDataService.CloseProjectData();
            await Task.CompletedTask;
        });

        // Wait until the project data is unloaded
        while (IsProjectDataLoaded)
        {
            await Task.Delay(50);
        }

        // Force the Workspace page to unload and destroy the workspace service
        _navigationService.NavigateToPage(EmptyPageName);

        // Wait until the workspace service is destroyed
        while (_userInterfaceService.IsWorkspaceLoaded)
        {
            await Task.Delay(50);
        }
    }
}

