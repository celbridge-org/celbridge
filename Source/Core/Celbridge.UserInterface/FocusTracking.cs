using Celbridge.Documents;
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
            typeof(FocusPanelId),
            typeof(FocusTracking),
            new PropertyMetadata(FocusPanelId.None));

    public static FocusPanelId GetPanel(DependencyObject element)
    {
        return (FocusPanelId)element.GetValue(PanelProperty);
    }

    public static void SetPanel(DependencyObject element, FocusPanelId value)
    {
        element.SetValue(PanelProperty, value);
    }

    /// <summary>
    /// Walks from the element towards the visual root and returns the nearest ancestor's Panel declaration,
    /// or None when no ancestor declares one. This is the same nearest-mapped-ancestor rule the focus tracker
    /// classifies a focused element by, so a subtree carries a single panel identity from its root.
    /// </summary>
    public static FocusPanelId FindPanel(DependencyObject element)
    {
        foreach (var ancestor in VisualTree.GetAncestors(element, includeSelf: true))
        {
            var panel = GetPanel(ancestor);
            if (panel != FocusPanelId.None)
            {
                return panel;
            }
        }

        return FocusPanelId.None;
    }

    /// <summary>
    /// Walks from the element towards the visual root and returns the nearest document view it sits inside,
    /// or null when it sits inside none. A document view is the root of its tab's content, so this is the
    /// rule that names the document an element belongs to.
    /// </summary>
    public static IDocumentView? FindDocumentView(DependencyObject element)
    {
        foreach (var ancestor in VisualTree.GetAncestors(element, includeSelf: true))
        {
            if (ancestor is IDocumentView documentView)
            {
                return documentView;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether the element is hosted in a popup (a flyout, context menu or content dialog) rather than in
    /// the window's main content. A popup hosts its content in a tree of its own, so the walk towards the
    /// root never passes the XamlRoot's content.
    /// </summary>
    public static bool IsPopupHosted(UIElement element)
    {
        var mainContentRoot = element.XamlRoot?.Content;
        if (mainContentRoot is null)
        {
            return false;
        }

        foreach (var ancestor in VisualTree.GetAncestors(element, includeSelf: true))
        {
            if (ReferenceEquals(ancestor, mainContentRoot))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The edit target that focus reports carry when this element's Panel declaration classifies the
    /// focused element. Set in code by panels that expose an edit target. Documents use
    /// IDocumentView.EditTarget instead, which the focus tracker prefers to this.
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
    /// off the panel; the Utility Panel rail is the current such element. Never declare it over a control
    /// the user clicks: a focused web surface then keeps the keyboard, and the focus reconcile that follows
    /// the click takes the control's pointer capture before it can raise Click.
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
