using Celbridge.CommonUI.Messages;
using Celbridge.CommonUI.UserInterface;
using Celbridge.CommonUI.Views;

namespace Celbridge.CommonUI.ViewModels;

public partial class MainPageViewModel : ObservableObject, INavigationProvider
{
    private IMessengerService _messengerService;
    private readonly IUserInterfaceService _userInterfaceService;

    public MainPageViewModel(IMessengerService messengerService, 
        IUserInterfaceService userInterfaceService)
    {
        _messengerService = messengerService;
        _userInterfaceService = userInterfaceService;
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
        _userInterfaceService.NavigateToPage(nameof(StartPage));

        // Todo: Add a user setting to automatically open the previously loaded project.
    }

    public void SelectNavigationItem_Home()
    {
        _userInterfaceService.NavigateToPage(nameof(StartPage));
    }

    public void SelectNavigationItem_NewProject()
    {
        _userInterfaceService.NavigateToPage(nameof(NewProjectPage));
    }

    public void SelectNavigationItem_OpenProject()
    {
        // Todo: Open a file picker dialog to select a project
    }

    public void SelectNavigationItem_Settings()
    {
        _userInterfaceService.NavigateToPage(nameof(SettingsPage));
    }
}

