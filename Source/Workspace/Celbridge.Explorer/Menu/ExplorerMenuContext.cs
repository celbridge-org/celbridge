using Celbridge.ContextMenu;
using Celbridge.DataTransfer;

namespace Celbridge.Explorer.Menu;

/// <summary>
/// Context for Explorer resource tree context menu operations. The project folder's key is empty.
/// </summary>
public record ExplorerMenuContext(
    ResourceItem ClickedResource,
    IReadOnlyList<ResourceItem> SelectedResources,
    bool IsProjectFolderTargeted,
    bool HasClipboardData,
    ClipboardContentType ClipboardContentType,
    ClipboardContentOperation ClipboardOperation
) : IMenuContext
{
    /// <summary>
    /// True when every selected resource can be deleted, renamed, moved, or cut
    /// (none locked, path-frozen, or under a read-only root). Precomputed when
    /// the context is built so the synchronous GetState can read it.
    /// </summary>
    public bool CanModifySelection { get; init; } = true;

    /// <summary>
    /// True when new resources can be added into the target folder (writable
    /// root, visible folder, folder not itself locked). Precomputed at build time.
    /// </summary>
    public bool CanAddToTargetFolder { get; init; } = true;

    /// <summary>
    /// True when exactly one resource is selected.
    /// </summary>
    public bool HasSingleSelection => SelectedResources.Count == 1;

    /// <summary>
    /// True when at least one resource is selected.
    /// </summary>
    public bool HasAnySelection => SelectedResources.Count > 0;

    /// <summary>
    /// True when exactly one item is selected OR the project folder is targeted via right-click.
    /// </summary>
    public bool IsSingleItemOrProjectFolderTargeted => HasSingleSelection || IsProjectFolderTargeted;

    /// <summary>
    /// Gets the single selected resource, or null if zero or multiple items are selected.
    /// </summary>
    public ResourceItem? SingleSelectedResource => HasSingleSelection ? SelectedResources[0] : null;

    /// <summary>
    /// True if any selected resource is the project folder.
    /// </summary>
    public bool SelectionContainsProjectFolder => SelectedResources.Any(resourceItem => resourceItem.Resource.IsEmpty);

    /// <summary>
    /// Resolves the target folder for operations: the clicked folder, or the parent folder of a clicked file.
    /// </summary>
    public ResourceKey GetTargetFolder()
    {
        return GetTargetFolder(ClickedResource);
    }

    /// <summary>
    /// Returns the folder for a new resource when the given resource is the target. A folder is its own
    /// target folder. A file's target folder is the file's parent folder.
    /// </summary>
    public static ResourceKey GetTargetFolder(ResourceItem resourceItem)
    {
        return resourceItem.IsFolder
            ? resourceItem.Resource
            : resourceItem.Resource.GetParent();
    }
}
