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

    private readonly IMessengerService _messengerService;
    private readonly ILoggingService _loggingService;
    private readonly INavigationService _navigationService;
    private readonly IProjectAdminService _projectAdminService;
    private readonly ISchedulerService _schedulerService;

    public MainPageViewModel(
        IMessengerService messengerService,
        ILoggingService loggingService, 
        INavigationService navigationService,
        IProjectAdminService projectAdminService,
        ISchedulerService schedulerService)
    {
        _messengerService = messengerService;
        _loggingService = loggingService;
        _navigationService = navigationService;
        _projectAdminService = projectAdminService;
        _schedulerService = schedulerService;
    }

    [ObservableProperty]
    private bool _isProjectLoaded;

    public event Func<Type, object, Result>? OnNavigate;

    public Result NavigateToPage(Type pageType, object parameter)
    {
        return OnNavigate?.Invoke(pageType, parameter)!;
    }

    public void OnMainPage_Loaded()
    {
        _messengerService.Register<WorkspaceInitializedMessage>(this, OnWorkspaceInitialized);
        // Todo: Register for project service destroyed message

        // Register this class as the navigation provider for the application
        var navigationService = _navigationService as NavigationService;
        Guard.IsNotNull(navigationService);
        navigationService.SetNavigationProvider(this);

        // Navigate to the start page at startup
        _navigationService.NavigateToPage("StartPage", string.Empty);

        // Todo: Add a user setting to automatically open the previously loaded project.
    }

    public void OnMainPage_Unloaded()
    {
        _messengerService.Unregister<WorkspaceInitializedMessage>(this);
    }

    private void OnWorkspaceInitialized(object recipient, WorkspaceInitializedMessage message)
    {
        IsProjectLoaded = true;
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

        if (tag == CloseProjectTag)
        {
            CloseProject();
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
            var projectAdminService = _projectAdminService; // Avoid capturing "this"

            CloseProject();

            _schedulerService.ScheduleFunction(async () =>
            {
                var projectName = Path.GetFileNameWithoutExtension(projectPath);
                Guard.IsNotNullOrWhiteSpace(projectName);

                projectAdminService.OpenProjectWorkspace(projectName, projectPath);
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
            var projectAdminService = _projectAdminService; // Avoid capturing "this"

            CloseProject();

            while (projectAdminService.LoadedProjectData is not null)
            {
                await Task.Delay(100);
            }

            _schedulerService.ScheduleFunction(async () =>
            {
                var projectName = Path.GetFileNameWithoutExtension(projectPath);
                Guard.IsNotNullOrWhiteSpace(projectName);

                projectAdminService.OpenProjectWorkspace(projectName, projectPath);
                await Task.CompletedTask;
            });

        }
    }

    private void CloseProject()
    {
        var projectAdminService = _projectAdminService; // Avoid capturing "this"

        // Todo: Notify the workspace that it is about to close.
        // The workspace may want to schedule some operations (e.g. save changes) before we close the project workspace.

        _schedulerService.ScheduleFunction(async () =>
        {
            projectAdminService.CloseProjectWorkspace();
            await Task.CompletedTask;
        });
    }
}

