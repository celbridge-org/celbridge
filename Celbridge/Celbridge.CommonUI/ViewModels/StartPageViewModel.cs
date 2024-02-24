using Celbridge.CommonServices.UserInterface;
using Celbridge.CommonUI.Views;
using CommunityToolkit.Mvvm.Input;

namespace Celbridge.CommonUI.ViewModels;

public partial class StartPageViewModel : ObservableObject
{
    private INavigationService _navigationService;

    public StartPageViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService;
    }

    public ICommand TestWorkspaceCommand => new RelayCommand(TestWorkspaceCommand_Executed);
    private void TestWorkspaceCommand_Executed()
    {
        _navigationService.NavigateToPage(nameof(WorkspacePage));
    }

    public ICommand LegacyInterfaceCommand => new RelayCommand(LegacyInterfaceCommand_Executed);
    private void LegacyInterfaceCommand_Executed()
    {
        _navigationService.NavigateToPage("Shell");
    }
}

