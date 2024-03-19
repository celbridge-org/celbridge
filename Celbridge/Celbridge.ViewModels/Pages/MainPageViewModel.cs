using Celbridge.BaseLibrary.Logging;
using Celbridge.BaseLibrary.Navigation;
using Celbridge.Services.UserInterface;
using CommunityToolkit.Diagnostics;

namespace Celbridge.ViewModels.Pages;

public partial class MainPageViewModel : ObservableObject, INavigationProvider
{
    public const string StartPageName = "StartPage";
    public const string NewProjectPageName = "NewProjectPage";
    public const string SettingsPageName = "SettingsPage";

    private ILoggingService _loggingService;
    private readonly INavigationService _navigationService;

    public MainPageViewModel(ILoggingService loggingService, 
        INavigationService navigationService)
    {
        _loggingService = loggingService;
        _navigationService = navigationService;
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
        _navigationService.NavigateToPage(StartPageName);

        // Todo: Add a user setting to automatically open the previously loaded project.
    }

    public void SelectNavigationItem(string pageName)
    {
        var navigationResult = _navigationService.NavigateToPage(pageName);
        if (navigationResult.IsSuccess)
        {
            return;
        }

        _loggingService.Error($"Failed to navigate to page {pageName}.");
    }
}

