using Celbridge.BaseLibrary.Logging;
using Celbridge.BaseLibrary.Project;
using Celbridge.BaseLibrary.Tasks;
using Celbridge.BaseLibrary.UserInterface;
using Celbridge.BaseLibrary.UserInterface.Navigation;
using Celbridge.Services.UserInterface.Navigation;
using CommunityToolkit.Diagnostics;

namespace Celbridge.ViewModels.Pages;

public partial class MainPageViewModel : ObservableObject, INavigationProvider
{
    public const string StartTag = "Start";
    public const string NewProjectTag = "NewProject";
    public const string OpenProjectTag = "OpenProject";
    public const string SettingsTag = "Settings";

    private readonly ILoggingService _loggingService;
    private readonly INavigationService _navigationService;
    private readonly IProjectDataService _projectDataService;
    private readonly ISchedulerService _schedulerService;

    public MainPageViewModel(ILoggingService loggingService, 
        INavigationService navigationService,
        IProjectDataService projectDataService,
        ISchedulerService schedulerService)
    {
        _loggingService = loggingService;
        _navigationService = navigationService;
        _projectDataService = projectDataService;
        _schedulerService = schedulerService;
    }

    public event Func<Type, object, Result>? OnNavigate;

    public Result NavigateToPage(Type pageType, object parameter)
    {
        return OnNavigate?.Invoke(pageType, parameter)!;
    }

    public void OnMainPage_Loaded()
    {
        // Register this class as the navigation provider for the application
        var navigationService = _navigationService as NavigationService;
        Guard.IsNotNull(navigationService);
        navigationService.SetNavigationProvider(this);

        // Navigate to the start page at startup
        _navigationService.NavigateToPage("StartPage", string.Empty);

        // Todo: Add a user setting to automatically open the previously loaded project.
    }

    public void SelectNavigationItem(string tag)
    {
        if (tag == NewProjectTag)
        {
            _ = NewProject();
            return;
        }

        if (tag == OpenProjectTag)
        {
            _ = OpenProject();
            return;
        }

        var navigationResult = _navigationService.NavigateToPage(tag, string.Empty);
        if (navigationResult.IsSuccess)
        {
            return;
        }

        _loggingService.Error($"Failed to navigate to item {tag}.");
    }

    private async Task NewProject()
    {
        var userInterfaceService = ServiceLocator.ServiceProvider.GetRequiredService<IUserInterfaceService>();
        var dialogService = userInterfaceService.DialogService;

        var showResult = await dialogService.ShowNewProjectDialogAsync();
        if (showResult.IsSuccess)
        {
            var projectPath = showResult.Value;
            var projectDataService = _projectDataService; // Avoid capturing this

            _schedulerService.ScheduleFunction(async () =>
            {
                // Todo: Close any open project first

                projectDataService.OpenProjectWorkspace(projectPath);
                await Task.CompletedTask;
            });
        }
    }

    private async Task OpenProject()
    {
        var userInterfaceService = ServiceLocator.ServiceProvider.GetRequiredService<IUserInterfaceService>();
        var filePickerService = userInterfaceService.FilePickerService;
        var result = await filePickerService.PickSingleFileAsync(new List<string> { ".celbridge" });
        if (result.IsSuccess)
        {
            var projectPath = result.Value;
            var projectDataService = _projectDataService; // Avoid capturing this

            _schedulerService.ScheduleFunction(async () =>
            {
                // Todo: Close any open project first

                projectDataService.OpenProjectWorkspace(projectPath);
                await Task.CompletedTask;
            });

        }
    }
}

