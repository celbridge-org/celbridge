namespace Celbridge.Workspace;

/// <summary>
/// Manages the views within the ProjectPanel.
/// </summary>
public interface IProjectPanelService
{
    /// <summary>
    /// The currently active view in the ProjectPanel.
    /// </summary>
    ProjectPanelView ActiveView { get; }

    /// <summary>
    /// Clears all registered ProjectPanel views.
    /// </summary>
    void ClearViews();

    /// <summary>
    /// Registers a UIElement as a view in the ProjectPanel.
    /// </summary>
    void RegisterView(ProjectPanelView view, UIElement element);

    /// <summary>
    /// Shows the specified view in the ProjectPanel, hiding all others.
    /// </summary>
    void ShowView(ProjectPanelView view);
}
