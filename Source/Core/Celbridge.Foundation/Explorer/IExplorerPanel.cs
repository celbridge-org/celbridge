namespace Celbridge.Explorer;

/// <summary>
/// Interface for interacting with the Explorer Panel view.
/// </summary>
public interface IExplorerPanel
{
    /// <summary>
    /// Moves keyboard focus into the panel's resource tree so the panel becomes the focused panel.
    /// </summary>
    void FocusPanel();

    /// <summary>
    /// Returns all selected resources in the explorer panel.
    /// </summary>
    List<ResourceKey> GetSelectedResources();

    /// <summary>
    /// Select resources in the explorer panel.
    /// </summary>
    Task<Result> SelectResources(List<ResourceKey> resources);
}
