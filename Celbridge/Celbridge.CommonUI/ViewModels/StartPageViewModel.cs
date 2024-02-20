using Celbridge.CommonUI.UserInterface;
using Celbridge.CommonUI.Views;
using CommunityToolkit.Mvvm.Input;

namespace Celbridge.CommonUI.ViewModels;

public partial class StartPageViewModel : ObservableObject
{
    private IUserInterfaceService _userInterfaceService;

    public StartPageViewModel(IUserInterfaceService userInterfaceService)
    {
        _userInterfaceService = userInterfaceService;
    }

    public ICommand TestWorkspaceCommand => new RelayCommand(TestWorkspaceCommand_Executed);
    private void TestWorkspaceCommand_Executed()
    {
        _userInterfaceService.NavigateToPage(nameof(WorkspacePage));
    }

    public ICommand LegacyInterfaceCommand => new RelayCommand(LegacyInterfaceCommand_Executed);
    private void LegacyInterfaceCommand_Executed()
    {
        _userInterfaceService.NavigateToPage(nameof(Legacy.Views.Shell));
    }
}

