using Celbridge.UserInterface.Helpers;

namespace Celbridge.WorkspaceUI;

/// <summary>
/// An attached property that declares which layout surface a container hosts. A panel derives the surface it
/// sits in by walking to the nearest ancestor carrying this property, so a panel's surface follows where the
/// layout mounts it rather than being hard-coded on the panel.
/// </summary>
public static class WorkspaceLayout
{
    public static readonly DependencyProperty SurfaceProperty =
        DependencyProperty.RegisterAttached(
            "Surface",
            typeof(WorkspaceSurface),
            typeof(WorkspaceLayout),
            new PropertyMetadata(WorkspaceSurface.None));

    public static WorkspaceSurface GetSurface(DependencyObject element)
    {
        return (WorkspaceSurface)element.GetValue(SurfaceProperty);
    }

    public static void SetSurface(DependencyObject element, WorkspaceSurface value)
    {
        element.SetValue(SurfaceProperty, value);
    }

    /// <summary>
    /// Walks from the element towards the visual root and returns the nearest ancestor's Surface declaration,
    /// or None when no ancestor is a surface container.
    /// </summary>
    public static WorkspaceSurface FindSurface(DependencyObject element)
    {
        foreach (var ancestor in VisualTree.GetAncestors(element, includeSelf: true))
        {
            var surface = GetSurface(ancestor);
            if (surface != WorkspaceSurface.None)
            {
                return surface;
            }
        }

        return WorkspaceSurface.None;
    }
}
