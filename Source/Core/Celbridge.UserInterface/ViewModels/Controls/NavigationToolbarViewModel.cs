using Celbridge.Navigation;

namespace Celbridge.UserInterface.ViewModels.Controls;

public partial class NavigationToolbarViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;

    public NavigationToolbarViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService;
    }

    public void NavigateToPage(string tag)
    {
        _navigationService.NavigateToPage(tag);
    }
}
