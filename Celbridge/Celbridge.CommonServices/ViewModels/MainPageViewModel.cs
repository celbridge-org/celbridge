using Celbridge.CommonServices.Messaging;
using Celbridge.CommonServices.UserInterface;

namespace Celbridge.CommonServices.ViewModels;

public partial class MainPageViewModel : ObservableObject, INavigationProvider
{
    private readonly string StartPageName = "StartPage";
    private readonly string NewProjectPageName = "NewProjectPage";
    private readonly string SettingsPageName = "SettingsPage";

    private IMessengerService _messengerService;
    private readonly INavigationService _navigationService;

    public MainPageViewModel(IMessengerService messengerService, 
        INavigationService navigationService)
    {
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

    public void SelectNavigationItem_Home()
    {
        _navigationService.NavigateToPage(StartPageName);
    }

    public void SelectNavigationItem_NewProject()
    {
        _navigationService.NavigateToPage(NewProjectPageName);
    }

    public void SelectNavigationItem_OpenProject()
    {
        // Todo: Open a file picker dialog to select a project
    }

    public void SelectNavigationItem_Settings()
    {
        _navigationService.NavigateToPage(SettingsPageName);
    }
}

