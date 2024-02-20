using Celbridge.CommonUI.UserInterface;

namespace Celbridge.CommonUI.ViewModels;

public partial class NewProjectPageViewModel : ObservableObject
{
    private IUserInterfaceService _userInterfaceService;

    public NewProjectPageViewModel(IUserInterfaceService userInterfaceService)
    {
        _userInterfaceService = userInterfaceService;
    }
}

