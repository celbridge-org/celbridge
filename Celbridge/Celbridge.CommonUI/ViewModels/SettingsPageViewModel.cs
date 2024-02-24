using Celbridge.CommonServices.UserInterface;

namespace Celbridge.CommonUI.ViewModels;

public partial class SettingsPageViewModel : ObservableObject
{
    private IUserInterfaceService _userInterfaceService;

    public SettingsPageViewModel(IUserInterfaceService userInterfaceService)
    {
        _userInterfaceService = userInterfaceService;
    }
}

