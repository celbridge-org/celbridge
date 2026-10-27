using Celbridge.Workspace;

namespace Celbridge.Explorer.Services;

/// <summary>
/// Manages folder UI state, such as expanded folder state in the resource tree.
/// </summary>
public class FolderStateService : IFolderStateService
{
    private const string ExpandedFoldersKey = "ExpandedFolders";

    private readonly IWorkspaceWrapper _workspaceWrapper;

    private readonly HashSet<ResourceKey> _expandedFolders = [];

    public IReadOnlySet<ResourceKey> ExpandedFolders => _expandedFolders;

    public FolderStateService(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public void SetExpanded(ResourceKey folderResource, bool isExpanded)
    {
        if (isExpanded)
        {
            if (!folderResource.IsEmpty)
            {
                _expandedFolders.Add(folderResource);
            }
        }
        else
        {
            _expandedFolders.Remove(folderResource);
        }
    }

    public bool IsExpanded(ResourceKey folderResource)
    {
        return _expandedFolders.Contains(folderResource);
    }

    public async Task LoadAsync()
    {
        var propertyBag = _workspaceWrapper.WorkspaceService.WorkspaceSettings.PropertyBag;
        Guard.IsNotNull(propertyBag);

        var expandedFolders = await propertyBag.GetPropertyAsync<List<string>>(ExpandedFoldersKey);
        if (expandedFolders is null)
        {
            return;
        }

        // A folder deleted while the tree did not list its parent stays in the saved list. Each load keeps only the
        // saved folders still on disk, so the list stays bounded by the project.
        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
        foreach (var expandedFolder in expandedFolders)
        {
            if (!ResourceKey.TryCreate(expandedFolder, out var folderResource))
            {
                continue;
            }

            var infoResult = await resourceFileSystem.GetInfoAsync(folderResource);
            if (infoResult.IsFailure
                || infoResult.Value.Kind != StorageItemKind.Folder)
            {
                continue;
            }

            SetExpanded(folderResource, true);
        }
    }

    public async Task SaveAsync()
    {
        var propertyBag = _workspaceWrapper.WorkspaceService.WorkspaceSettings.PropertyBag;
        Guard.IsNotNull(propertyBag);

        // The folders are saved in key order, so the same folders produce the same file.
        var expandedFolders = _expandedFolders.Order().Select(folder => folder.ToString()).ToList();
        await propertyBag.SetPropertyAsync(ExpandedFoldersKey, expandedFolders);
    }
}
