using Celbridge.BaseLibrary.Logging;
using Celbridge.BaseLibrary.Project;
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
    private readonly IProjectManagerService _projectManagerService;

    public MainPageViewModel(ILoggingService loggingService, 
        INavigationService navigationService,
        IProjectManagerService projectManagerService)
    {
        _loggingService = loggingService;
        _navigationService = navigationService;
        _projectManagerService = projectManagerService;
    }

    public event Func<Type, Result>? OnNavigate;

    public Result NavigateToPage(Type pageType)
    {
        return OnNavigate?.Invoke(pageType)!;
    }

    public void OnMainPage_Loaded()
    {
        // Register this class as the navigation provider for the application
        var navigationService = _navigationService as NavigationService;
        Guard.IsNotNull(navigationService);
        navigationService.SetNavigationProvider(this);

        // Navigate to the start page at startup
        _navigationService.NavigateToPage("StartPage");

        // Todo: Add a user setting to automatically open the previously loaded project.
    }

    public void SelectNavigationItem(string tag)
    {
        if (tag == NewProjectTag)
        {
            var userInterfaceService = ServiceLocator.ServiceProvider.GetRequiredService<IUserInterfaceService>();
            var dialogService = userInterfaceService.DialogService;
            dialogService.ShowNewProjectDialogAsync();
            return;
        }

        if (tag == OpenProjectTag)
        {
            _ = OpenProject();
            return;
        }

        var navigationResult = _navigationService.NavigateToPage(tag);
        if (navigationResult.IsSuccess)
        {
            return;
        }

        _loggingService.Error($"Failed to navigate to item {tag}.");
    }

    private async Task OpenProject()
    {
        var userInterfaceService = ServiceLocator.ServiceProvider.GetRequiredService<IUserInterfaceService>();
        var filePickerService = userInterfaceService.FilePickerService;
        var result = await filePickerService.PickSingleFileAsync(new List<string> { ".celbridge" });
        if (result.IsSuccess)
        {
            var projectPath = result.Value;
            _projectManagerService.OpenProject(projectPath);
        } 
    }
}

