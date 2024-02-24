using Celbridge.CommonServices.UserInterface;

namespace Celbridge.CommonUI.ViewModels;

public partial class SettingsPageViewModel : ObservableObject
{
    private INavigationService _navigationService;

    public SettingsPageViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService;
    }
}
