using Celbridge.UserInterface.Helpers;

namespace Celbridge.WorkspaceUI;

/// <summary>
/// An attached property that declares which layout region a container hosts. A panel derives the region it
/// sits in by walking to the nearest ancestor carrying this property, so a panel's region follows where the
/// layout mounts it rather than being hard-coded on the panel.
/// </summary>
public static class WorkspaceLayout
{
    public static readonly DependencyProperty RegionProperty =
        DependencyProperty.RegisterAttached(
            "Region",
            typeof(LayoutRegion),
            typeof(WorkspaceLayout),
            new PropertyMetadata(LayoutRegion.None));

    public static LayoutRegion GetRegion(DependencyObject element)
    {
        return (LayoutRegion)element.GetValue(RegionProperty);
    }

    public static void SetRegion(DependencyObject element, LayoutRegion value)
    {
        element.SetValue(RegionProperty, value);
    }

    /// <summary>
    /// Walks from the element towards the visual root and returns the nearest ancestor's Region declaration,
    /// or None when no ancestor is a region container.
    /// </summary>
    public static LayoutRegion FindRegion(DependencyObject element)
    {
        foreach (var ancestor in VisualTree.GetAncestors(element, includeSelf: true))
        {
            var region = GetRegion(ancestor);
            if (region != LayoutRegion.None)
            {
                return region;
            }
        }

        return LayoutRegion.None;
    }
}
