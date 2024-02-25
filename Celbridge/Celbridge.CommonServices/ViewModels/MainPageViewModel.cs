using Celbridge.CommonServices.Messaging;
using Celbridge.CommonServices.UserInterface;

namespace Celbridge.CommonServices.ViewModels;

public partial class MainPageViewModel : ObservableObject, INavigationProvider
{
    public const string StartPageName = "StartPage";
    public const string NewProjectPageName = "NewProjectPage";
    public const string OpenProjectPageName = "OpenProjectPage";
    public const string LegacyPageName = "Shell";
    public const string SettingsPageName = "SettingsPage";

    private ILoggingService _loggingService;
    private IMessengerService _messengerService;
    private readonly INavigationService _navigationService;

    public MainPageViewModel(ILoggingService loggingService,
        IMessengerService messengerService, 
        INavigationService navigationService)
    {
        _loggingService = loggingService;
        _messengerService = messengerService;
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
        var message = new NavigationProviderLoadedMessage(this);
        _messengerService.Send(message);

        // Navigate to the start page at startup
        _navigationService.NavigateToPage(StartPageName);

        // Todo: Add a user setting to automatically open the previously loaded project.
    }

    public void SelectNavigationItem(string navigationItem)
    {
        var navigateResult = _navigationService.NavigateToPage(navigationItem);
        if (navigateResult.IsSuccess)
        {
            return;
        }

        // Todo: Handle navigation to non-page items

        _loggingService.Error($"Failed to navigate to unknown navigation item '{navigationItem}'");
    }
}

