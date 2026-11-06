using Microsoft.UI.Xaml.Media;
using KeyRoutedEventArgs = Microsoft.UI.Xaml.Input.KeyRoutedEventArgs;
using VirtualKey = Windows.System.VirtualKey;

namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Keyboard focus navigation helpers shared by the views and elements that move focus in response to input.
/// </summary>
public static class FocusNavigationHelper
{
    /// <summary>
    /// Moves keyboard focus from the given element to the next focusable one in tab order, as pressing Tab
    /// would, wrapping back to the first at the end. Does nothing for an element outside a loaded tree.
    /// </summary>
    public static void MoveFocusToNextElement(DependencyObject current)
    {
        MoveFocus(current, backwards: false);
    }

    /// <summary>
    /// Moves keyboard focus from the given element along the tab order, forwards or backwards, wrapping at
    /// either end. Does nothing for an element outside a loaded tree.
    /// </summary>
    public static void MoveFocus(DependencyObject current, bool backwards)
    {
        // UNO-BUG: the FindNextElementOptions overload of TryMoveFocus throws for Next, Previous and None.
        // Neither FocusManager overload can do this on both heads, so neither is used: the packaged WinUI
        // head rejects TryMoveFocus without options ("Catastrophic failure", it wants the options overload
        // with a loaded SearchRoot), and the Skia head throws for the options overload. Walking the tree for
        // the next tab stop behaves the same on both.
        var tabStops = GetTabStops(FindTabOrderRoot(current)).ToList();

        var currentIndex = tabStops.FindIndex(tabStop => ReferenceEquals(tabStop, current));
        if (currentIndex < 0)
        {
            return;
        }

        var step = backwards ? tabStops.Count - 1 : 1;
        var nextTabStop = tabStops[(currentIndex + step) % tabStops.Count];

        nextTabStop.Focus(FocusState.Programmatic);
    }

    // Returns the content of the open popup around the element, so a field inside a dialog stays inside the dialog.
    // On the Skia heads, the root of a popup's tree also contains the window content. An element outside any popup uses
    // the root of the element's own tree.
    private static DependencyObject FindTabOrderRoot(DependencyObject element)
    {
        var ancestors = VisualTree.GetAncestors(element, includeSelf: true).ToList();

        if (element is UIElement { XamlRoot: { } xamlRoot })
        {
            foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot))
            {
                if (popup.Child is { } popupContent
                    && ancestors.Contains(popupContent))
                {
                    return popupContent;
                }
            }
        }

        return ancestors.Last();
    }

    /// <summary>
    /// Gives keyboard focus to the first focusable element under the given one, in tab order, and reports
    /// whether one took it.
    /// </summary>
    public static bool TryFocusFirstElement(DependencyObject root)
    {
        foreach (var tabStop in GetTabStops(root))
        {
            if (tabStop.Focus(FocusState.Programmatic))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Handles Enter as a field commit, by moving focus the way a Tab would. A field bound on lost focus
    /// commits either way, so this is what shows the user that it did. Leaves any other key alone.
    /// </summary>
    public static void CommitFieldOnEnter(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;

        if (sender is not DependencyObject field)
        {
            return;
        }

        MoveFocusToNextElement(field);
    }

    // The focusable controls under an element, in the depth-first order the tab order follows for a tree
    // that does not reorder itself with TabIndex. A collapsed element takes its whole subtree with it.
    private static IEnumerable<Control> GetTabStops(DependencyObject element)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(element);

        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(element, index);

            if (child is FrameworkElement childElement
                && childElement.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            if (child is Control control
                && control.IsEnabled
                && control.IsTabStop)
            {
                yield return control;
            }

            foreach (var descendant in GetTabStops(child))
            {
                yield return descendant;
            }
        }
    }
}
