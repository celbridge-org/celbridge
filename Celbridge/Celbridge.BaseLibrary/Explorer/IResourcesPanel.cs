namespace Celbridge.Explorer;

/// <summary>
/// Interface for interacting with the DocumentsPanel view.
/// </summary>
public interface IResourcesPanel
{
    /// <summary>
    /// Returns the selected resource in the explorer panel.
    /// Returns an empty resource if no resource is currently selected.
    /// </summary>
    ResourceKey GetSelectedResource();

    /// <summary>
    /// Select a resource in the explorer panel.
    /// </summary>
    Result SetSelectedResource(ResourceKey resource);
}
