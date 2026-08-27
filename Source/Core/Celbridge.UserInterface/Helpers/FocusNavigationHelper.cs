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
        // UNO-BUG: the FindNextElementOptions overload of TryMoveFocus throws for Next, Previous and None.
        // Neither FocusManager overload can do this on both heads, so neither is used: the packaged WinUI
        // head rejects TryMoveFocus without options ("Catastrophic failure", it wants the options overload
        // with a loaded SearchRoot), and the Skia head throws for the options overload. Walking the tree for
        // the next tab stop behaves the same on both.
        //
        // Searched from the root of the element's own tree rather than from the XamlRoot content, so a
        // field inside a dialog searches the popup holding it rather than the window behind it.
        var searchRoot = VisualTree.GetAncestors(current, includeSelf: true).Last();

        var tabStops = GetTabStops(searchRoot).ToList();

        var currentIndex = tabStops.FindIndex(tabStop => ReferenceEquals(tabStop, current));
        if (currentIndex < 0)
        {
            return;
        }

        var nextTabStop = tabStops[(currentIndex + 1) % tabStops.Count];

        nextTabStop.Focus(FocusState.Programmatic);
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
