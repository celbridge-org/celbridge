using Celbridge.Documents;
using Celbridge.Workspace;

namespace Celbridge.UserInterface;

/// <summary>
/// Where a focused element sits relative to the window's content.
/// </summary>
public enum FocusHost
{
    /// <summary>
    /// The window's main content.
    /// </summary>
    MainContent,

    /// <summary>
    /// An open popup: a flyout, a context menu or a content dialog.
    /// </summary>
    Popup,

    /// <summary>
    /// Nothing at all. Uno leaves managed focus on a dismissed popup's item after taking that item out of
    /// the visual tree, so the keyboard can rest on an element the user can no longer see or reach.
    /// </summary>
    Detached
}

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
    /// Where the element sits relative to the window's content.
    /// </summary>
    public static FocusHost GetFocusHost(UIElement element)
    {
        var mainContentRoot = element.XamlRoot?.Content;
        if (mainContentRoot is null)
        {
            return FocusHost.Detached;
        }

        DependencyObject? topmost = null;
        foreach (var ancestor in VisualTree.GetAncestors(element, includeSelf: true))
        {
            if (ReferenceEquals(ancestor, mainContentRoot))
            {
                return FocusHost.MainContent;
            }

            topmost = ancestor;
        }

        // A popup hosts its content in a tree of its own, so the walk misses the window content but still
        // reaches the root the window hangs from, by way of the popup root.
        return ReferenceEquals(topmost, GetVisualRoot(mainContentRoot))
            ? FocusHost.Popup
            : FocusHost.Detached;
    }

    // The root the whole window hangs from.
    private static DependencyObject? GetVisualRoot(UIElement mainContentRoot)
    {
        DependencyObject? visualRoot = null;
        foreach (var ancestor in VisualTree.GetAncestors(mainContentRoot, includeSelf: true))
        {
            visualRoot = ancestor;
        }

        return visualRoot;
    }

    /// <summary>
    /// The edit target that focus reports carry when this element's Panel declaration classifies the
    /// focused element.
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
