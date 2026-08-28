using Celbridge.UserInterface.Helpers;

namespace Celbridge.WorkspaceUI;

/// <summary>
/// An attached property that declares which workspace area a container hosts. A panel derives the area it
/// sits in by walking to the nearest ancestor carrying this property, so a panel's area follows where the
/// layout mounts it rather than being hard-coded on the panel.
/// </summary>
public static class WorkspaceLayout
{
    public static readonly DependencyProperty AreaProperty =
        DependencyProperty.RegisterAttached(
            "Area",
            typeof(WorkspaceArea?),
            typeof(WorkspaceLayout),
            new PropertyMetadata(null));

    public static WorkspaceArea? GetArea(DependencyObject element)
    {
        return (WorkspaceArea?)element.GetValue(AreaProperty);
    }

    public static void SetArea(DependencyObject element, WorkspaceArea? value)
    {
        element.SetValue(AreaProperty, value);
    }

    /// <summary>
    /// Walks from the element towards the visual root and returns the nearest ancestor's Area declaration,
    /// or null when no ancestor is an area container.
    /// </summary>
    public static WorkspaceArea? FindArea(DependencyObject element)
    {
        foreach (var ancestor in VisualTree.GetAncestors(element, includeSelf: true))
        {
            var area = GetArea(ancestor);
            if (area is not null)
            {
                return area;
            }
        }

        return null;
    }
}
