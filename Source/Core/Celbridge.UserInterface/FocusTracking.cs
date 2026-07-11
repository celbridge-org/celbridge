using Celbridge.Workspace;

namespace Celbridge.UserInterface;

/// <summary>
/// Attached properties that declare which workspace panel a UI subtree belongs to. The central focus
/// tracker classifies a focused element by its nearest ancestor carrying FocusTracking.Panel, so panel
/// roots declare the property once instead of reporting focus themselves.
/// </summary>
public static class FocusTracking
{
    public static readonly DependencyProperty PanelProperty =
        DependencyProperty.RegisterAttached(
            "Panel",
            typeof(WorkspacePanel),
            typeof(FocusTracking),
            new PropertyMetadata(WorkspacePanel.None));

    public static WorkspacePanel GetPanel(DependencyObject element)
    {
        return (WorkspacePanel)element.GetValue(PanelProperty);
    }

    public static void SetPanel(DependencyObject element, WorkspacePanel value)
    {
        element.SetValue(PanelProperty, value);
    }

    /// <summary>
    /// The edit target that focus reports carry when this element's Panel declaration classifies the
    /// focused element. Set in code by panels that expose an edit target.
    /// </summary>
    public static readonly DependencyProperty EditTargetProperty =
        DependencyProperty.RegisterAttached(
            "EditTarget",
            typeof(IEditTarget),
            typeof(FocusTracking),
            new PropertyMetadata(null));

    public static IEditTarget? GetEditTarget(DependencyObject element)
    {
        return (IEditTarget?)element.GetValue(EditTargetProperty);
    }

    public static void SetEditTarget(DependencyObject element, IEditTarget? value)
    {
        element.SetValue(EditTargetProperty, value);
    }
}
