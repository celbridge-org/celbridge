using Celbridge.Workspace;

namespace Celbridge.Explorer.Services;

/// <summary>
/// Manages folder UI state, such as expanded folder state in the resource tree.
/// </summary>
public class FolderStateService : IFolderStateService
{
    private const string ExpandedFoldersKey = "ExpandedFolders";

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public List<string> ExpandedFolders { get; } = [];

    public FolderStateService(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public void SetExpanded(ResourceKey folderResource, bool isExpanded)
    {
        if (isExpanded)
        {
            if (!folderResource.IsEmpty &&
                !ExpandedFolders.Contains(folderResource))
            {
                ExpandedFolders.Add(folderResource);
                ExpandedFolders.Sort();
            }
        }
        else
        {
            ExpandedFolders.Remove(folderResource);
        }
    }

    public bool IsExpanded(ResourceKey folderResource)
    {
        return ExpandedFolders.Contains(folderResource);
    }

    public void Cleanup()
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        ExpandedFolders.Remove(string.Empty);
        ExpandedFolders.RemoveAll(expandedFolder => resourceRegistry.GetResource(expandedFolder).IsFailure);
    }

    public async Task LoadAsync()
    {
        var workspaceSettings = _workspaceWrapper.WorkspaceService.WorkspaceSettings;
        var expandedFolders = await workspaceSettings.GetPropertyAsync<List<string>>(ExpandedFoldersKey);
        if (expandedFolders is not null &&
            expandedFolders.Count > 0)
        {
            foreach (var expandedFolder in expandedFolders)
            {
                SetExpanded(expandedFolder, true);
            }
        }
    }

    public async Task SaveAsync()
    {
        var workspaceSettings = _workspaceWrapper.WorkspaceService.WorkspaceSettings;
        await workspaceSettings.SetPropertyAsync(ExpandedFoldersKey, ExpandedFolders);
    }
}
