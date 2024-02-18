using Celbridge.CommonUI.UserInterface;

namespace Celbridge.CommonUI.ViewModels;

public partial class StartViewModel : ObservableObject
{
    private readonly IUserInterfaceService _userInterfaceService;

    public StartViewModel(IUserInterfaceService userInterfaceService)
    {
        _userInterfaceService = userInterfaceService;
    }
}

