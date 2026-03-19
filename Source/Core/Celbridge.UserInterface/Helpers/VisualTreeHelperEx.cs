namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Provides utility methods for traversing the visual tree.
/// </summary>
public static class VisualTreeHelperEx
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
}
