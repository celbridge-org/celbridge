using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.WorkspaceUI.ViewModels;

public partial class ActivityPanelViewModel : ObservableObject
{
    [ObservableProperty]
    private ActivityPanelTab _currentTab = ActivityPanelTab.None;

    [ObservableProperty]
    private bool _hasShortcuts;

    public ActivityPanelViewModel()
    {
    }
}
