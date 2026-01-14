namespace Celbridge.Workspace.Services;

/// <summary>
/// Manages the views within the ProjectPanel.
/// </summary>
public class ProjectPanelService : IProjectPanelService
{
    private Dictionary<ProjectPanelView, UIElement> _viewElements = [];

    public ProjectPanelView ActiveView { get; private set; } = ProjectPanelView.None;

    public void ClearViews()
    {
        _viewElements.Clear();
        ActiveView = ProjectPanelView.None;
    }

    public void RegisterView(ProjectPanelView view, UIElement element)
    {
        _viewElements[view] = element;
        element.Visibility = Visibility.Collapsed;
    }

    public void ShowView(ProjectPanelView view)
    {
        foreach (var pair in _viewElements)
        {
            pair.Value.Visibility = pair.Key == view 
                ? Visibility.Visible 
                : Visibility.Collapsed;
        }
        ActiveView = view;
    }
}
