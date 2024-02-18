using Celbridge.CommonUI.UserInterface;
using Celbridge.CommonUI.Views;

namespace Celbridge.CommonUI.ViewModels;

public partial class MainPageViewModel : ObservableObject
{
    private readonly IUserInterfaceService _userInterfaceService;

    public MainPageViewModel(IUserInterfaceService userInterfaceService)
    {
        _userInterfaceService = userInterfaceService;
    }

    public void SelectNavigationItem_Home()
    {
        _userInterfaceService.Navigate<StartView>();
    }

    public void SelectNavigationItem_NewProject()
    {
    }

    public void SelectNavigationItem_OpenProject()
    {
    }

    public void SelectNavigationItem_Settings()
    {
    }
}

