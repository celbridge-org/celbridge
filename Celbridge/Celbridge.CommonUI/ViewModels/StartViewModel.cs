using Celbridge.CommonUI.UserInterface;
using Celbridge.CommonUI.Views;
using CommunityToolkit.Mvvm.Input;

namespace Celbridge.CommonUI.ViewModels;

public partial class StartViewModel : ObservableObject
{
    private readonly IUserInterfaceService _userInterfaceService;

    public StartViewModel(IUserInterfaceService userInterfaceService)
    {
        _userInterfaceService = userInterfaceService;
    }

    public ICommand SelectNewUICommand => new RelayCommand(SelectNewUICommand_Executed);
    private void SelectNewUICommand_Executed()
    {
        _userInterfaceService.Navigate(typeof(WorkspaceView));
    }

    public ICommand SelectLegacyUICommand => new RelayCommand(SelectLegacyUICommand_Executed);
    private void SelectLegacyUICommand_Executed()
    {
        // Todo: Remove legacy type and project reference
        _userInterfaceService.Navigate(typeof(Legacy.Views.Shell));
    }
}

