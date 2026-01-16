using Celbridge.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Celbridge.Workspace.ViewModels;

public partial class ProjectPanelViewModel : ObservableObject
{
    [ObservableProperty]
    private ProjectPanelTab _currentTab = ProjectPanelTab.None;

    [ObservableProperty]
    private bool _hasShortcuts;

    public ProjectPanelViewModel()
    {
    }
}
