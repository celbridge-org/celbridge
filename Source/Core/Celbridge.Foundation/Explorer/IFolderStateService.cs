namespace Celbridge.Explorer;

/// <summary>
/// Manages the expanded folder state in the resource tree.
/// </summary>
public interface IFolderStateService
{
    /// <summary>
    /// Returns the list of expanded folders in the resource tree.
    /// </summary>
    List<string> ExpandedFolders { get; }

    /// <summary>
    /// Mark a folder resource as expanded or collapsed in the resource tree.
    /// </summary>
    void SetExpanded(ResourceKey folderResource, bool isExpanded);

    /// <summary>
    /// Returns true if the folder with the specified resource key is expanded.
    /// </summary>
    bool IsExpanded(ResourceKey folderResource);

    /// <summary>
    /// Removes expanded folder entries that no longer exist in the resource registry.
    /// </summary>
    void Cleanup();

    /// <summary>
    /// Loads the folder state from workspace settings.
    /// </summary>
    Task LoadAsync();

    /// <summary>
    /// Saves the folder state to workspace settings.
    /// </summary>
    Task SaveAsync();
}
