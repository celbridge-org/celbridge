using Celbridge.Foundation;

namespace Celbridge.Explorer;

/// <summary>
/// Interface for interacting with the Explorer Panel view.
/// </summary>
public interface IExplorerPanel
{
    /// <summary>
    /// Returns the selected resource in the explorer panel.
    /// Returns an empty resource if no resource is currently selected.
    /// </summary>
    ResourceKey GetSelectedResource();

    /// <summary>
    /// Select a resource in the explorer panel.
    /// Automatically expands the folders containing the resource.
    /// </summary>
    Task<Result> SelectResource(ResourceKey resource);
}
