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

    /// <summary>
    /// Updates the resource tree's rows, listing each shown folder the tree has no listing of yet.
    /// </summary>
    Task RefreshTreeAsync();

    /// <summary>
    /// Expands or collapses a folder in the resource tree.
    /// </summary>
    Task SetFolderExpandedAsync(ResourceKey folder, bool isExpanded);

    /// <summary>
    /// Collapses every folder in the resource tree.
    /// </summary>
    Task CollapseAllFoldersAsync();
}
