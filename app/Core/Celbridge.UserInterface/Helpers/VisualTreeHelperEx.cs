namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Provides utility methods for traversing the visual tree.
/// </summary>
public static class VisualTreeHelperEx
{
    /// <summary>
    /// Finds the first descendant of the specified type in the visual tree.
    /// </summary>
    /// <typeparam name="T">The type of descendant to find.</typeparam>
    /// <param name="parent">The parent element to start searching from.</param>
    /// <returns>The first descendant of the specified type, or null if not found.</returns>
    public static T? FindDescendant<T>(DependencyObject parent) where T : class
    {
        int childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
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
    /// <param name="parent">The parent element to start searching from.</param>
    /// <param name="name">The name of the descendant to find.</param>
    /// <returns>The first descendant with the specified name, or null if not found.</returns>
    public static DependencyObject? FindDescendantByName(DependencyObject parent, string name)
    {
        int childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
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
