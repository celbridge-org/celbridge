using Celbridge.ContextMenu;
using Celbridge.DataTransfer;

namespace Celbridge.Explorer.Menu;

/// <summary>
/// Context for Explorer resource tree context menu operations.
/// </summary>
public record ExplorerMenuContext(
    IResource? ClickedResource,
    IReadOnlyList<IResource> SelectedResources,
    IFolderResource ProjectFolder,
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
    public IResource? SingleSelectedResource => HasSingleSelection ? SelectedResources[0] : null;

    /// <summary>
    /// True if any selected resource is the project folder.
    /// </summary>
    public bool SelectionContainsProjectFolder => SelectedResources.Any(r => r == ProjectFolder);

    /// <summary>
    /// Resolves the target folder for operations based on the clicked resource or selection.
    /// Returns the clicked/selected folder, the parent folder of a clicked/selected file, or the project folder.
    /// </summary>
    public IFolderResource GetTargetFolder()
    {
        var target = ClickedResource ?? (HasSingleSelection ? SingleSelectedResource : null);

        if (target is IFolderResource folderResource)
        {
            return folderResource;
        }
        else if (target is IFileResource fileResource && fileResource.ParentFolder != null)
        {
            return fileResource.ParentFolder;
        }

        return ProjectFolder;
    }
}
