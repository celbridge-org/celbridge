using Celbridge.BaseLibrary.UserInterface;
using Celbridge.CommonServices.UserInterface;

namespace Celbridge.CommonViewModels.Pages;

public partial class StartPageViewModel : ObservableObject
{
    private readonly string WorkspacePageName = "WorkspacePage";
    private readonly string ShellName = "Shell";

    private INavigationService _navigationService;

    public StartPageViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService;
    }

    public ICommand TestWorkspaceCommand => new RelayCommand(TestWorkspaceCommand_Executed);
    private void TestWorkspaceCommand_Executed()
    {
        _navigationService.NavigateToPage(WorkspacePageName);
    }

    public ICommand LegacyInterfaceCommand => new RelayCommand(LegacyInterfaceCommand_Executed);
    private void LegacyInterfaceCommand_Executed()
    {
        _navigationService.NavigateToPage(ShellName);
    }
}

