using Celbridge.CommonServices.UserInterface;

namespace Celbridge.CommonUI.ViewModels;

public partial class NewProjectPageViewModel : ObservableObject
{
    private INavigationService _navigationService;

    public NewProjectPageViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService;
    }
}

