using Celbridge.BaseLibrary.UserInterface;
using Celbridge.Services.UserInterface;

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

    public ICommand OpenWorkspacePageCommand => new RelayCommand(OpenWorkspacePageCommand_Executed);
    private void OpenWorkspacePageCommand_Executed()
    {
        _navigationService.NavigateToPage(WorkspacePageName);
    }

    public ICommand LegacyInterfaceCommand => new RelayCommand(LegacyInterfaceCommand_Executed);
    private void LegacyInterfaceCommand_Executed()
    {
        _navigationService.NavigateToPage(ShellName);
    }
}

