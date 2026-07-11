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

    /// <summary>
    /// Marks a subtree where focus landing preserves the currently focused panel instead of clearing it to
    /// None. Declared on chrome that can transiently receive focus without representing a deliberate move
    /// off the panel; the activity rail is the current such element.
    /// </summary>
    public static readonly DependencyProperty PreservePanelFocusProperty =
        DependencyProperty.RegisterAttached(
            "PreservePanelFocus",
            typeof(bool),
            typeof(FocusTracking),
            new PropertyMetadata(false));

    public static bool GetPreservePanelFocus(DependencyObject element)
    {
        return (bool)element.GetValue(PreservePanelFocusProperty);
    }

    public static void SetPreservePanelFocus(DependencyObject element, bool value)
    {
        element.SetValue(PreservePanelFocusProperty, value);
    }
}
