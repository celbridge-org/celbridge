using Celbridge.CommonUI.UserInterface;

namespace Celbridge.CommonUI.ViewModels;

public partial class MainPageViewModel : ObservableObject
{
    private readonly IUserInterfaceService _userInterfaceService;

    public MainPageViewModel(IUserInterfaceService userInterfaceService)
    {
        _userInterfaceService = userInterfaceService;
    }
}

