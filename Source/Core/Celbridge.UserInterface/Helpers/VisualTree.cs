namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Utility methods for traversing the visual tree, both towards descendants and towards ancestors.
/// </summary>
public static class VisualTree
{
    /// <summary>
    /// Finds the first descendant of the specified type in the visual tree.
    /// </summary>
    public static T? FindDescendant<T>(DependencyObject parent) where T : class
    {
        int childCount = VisualTreeHelper.GetChildrenCount(parent);

        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result)
            {
                return result;
            }

            var descendant = FindDescendant<T>(child);
            if (descendant != null)
            {
                return descendant;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the first descendant with the specified name in the visual tree.
    /// </summary>
    public static DependencyObject? FindDescendantByName(DependencyObject parent, string name)
    {
        int childCount = VisualTreeHelper.GetChildrenCount(parent);

        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement element && element.Name == name)
            {
                return element;
            }

            var descendant = FindDescendantByName(child, name);
            if (descendant != null)
            {
                return descendant;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the first descendant whose AutomationProperties.AutomationId matches
    /// the specified id in the visual tree. Reads the managed attached property
    /// directly, so it works regardless of whether native automation mapping is
    /// enabled.
    /// </summary>
    public static FrameworkElement? FindDescendantByAutomationId(DependencyObject parent, string automationId)
    {
        int childCount = VisualTreeHelper.GetChildrenCount(parent);

        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement element &&
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(element) == automationId)
            {
                return element;
            }

            var descendant = FindDescendantByAutomationId(child, automationId);
            if (descendant != null)
            {
                return descendant;
            }
        }

        return null;
    }

    /// <summary>
    /// Enumerates the ancestors of the element, from its parent up towards the visual root. When includeSelf
    /// is set, the element itself is yielded first. Lazy, so a caller that stops at the first match does not
    /// walk the rest of the chain.
    /// </summary>
    public static IEnumerable<DependencyObject> GetAncestors(DependencyObject element, bool includeSelf = false)
    {
        DependencyObject? current;
        if (includeSelf)
        {
            current = element;
        }
        else
        {
            current = VisualTreeHelper.GetParent(element);
        }

        while (current is not null)
        {
            yield return current;

            current = VisualTreeHelper.GetParent(current);
        }
    }
}
