using Celbridge.UserInterface;
using Celbridge.Workspace;

namespace Celbridge.Resources.Services;

/// <summary>
/// Builds the in-memory project tree by enumerating the project root through the
/// resource file-system gateway. The walk stops at the project's search-exclude
/// patterns, so a build folder inside the project costs nothing to maintain.
/// </summary>
public sealed class ProjectTreeBuilder : IProjectTreeBuilder
{
    private readonly IIconService _iconService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ProjectTreeBuilder(
        IIconService iconService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _iconService = iconService;
        _workspaceWrapper = workspaceWrapper;
    }

    public async Task<Result<IFolderResource>> BuildTreeAsync()
    {
        var root = new FolderResource(string.Empty, null);

        var synchronizeResult = await SynchronizeFolderAsync(root, ResourceKey.Empty);
        if (synchronizeResult.IsFailure)
        {
            return Result<IFolderResource>.Fail("Failed to build the project tree.")
                .WithErrors(synchronizeResult);
        }

        return Result<IFolderResource>.Ok(root);
    }

    private async Task<Result> SynchronizeFolderAsync(FolderResource folderResource, ResourceKey folderKey)
    {
        var resourceService = _workspaceWrapper.WorkspaceService.ResourceService;
        var resourceFileSystem = resourceService.FileSystem;
        var policy = resourceService.Policy;
        var rootHandlerRegistry = resourceService.RootHandlers;

        var enumerateResult = await resourceFileSystem.EnumerateFolderAsync(folderKey);
        if (enumerateResult.IsFailure)
        {
            return Result.Fail($"Failed to enumerate folder: '{folderKey}'")
                .WithErrors(enumerateResult);
        }
        var folderChildren = enumerateResult.Value;

        // Children rebuild from scratch on every call so stale TreeViewNode.Content
        // references do not survive a rapid undo/redo cycle.
        folderResource.Children.Clear();

        foreach (var folderChild in folderChildren)
        {
            // Search scope bounds the walk until the resource index lands, because every
            // mutating command re-walks whatever the tree holds. Once the index applies
            // deltas instead, search-exclude goes back to binding the scanners alone.
            if (policy.IsSearchExcluded(folderChild.Resource, folderChild.IsFolder))
            {
                continue;
            }

            var childName = folderChild.Resource.ResourceName;
            var writableState = WritableStatePriority.Compute(
                folderChild.Resource,
                folderChild.Attributes,
                rootHandlerRegistry);

            if (folderChild.IsFolder)
            {
                var childFolder = new FolderResource(childName, folderResource);
                childFolder.WritableState = writableState;

                var childResult = await SynchronizeFolderAsync(childFolder, folderChild.Resource);
                if (childResult.IsFailure)
                {
                    return childResult;
                }

                folderResource.AddChild(childFolder);
            }
            else
            {
                var getIconResult = _iconService.GetFileIconForFileName(childName);
                var iconDefinition = getIconResult.IsSuccess
                    ? getIconResult.Value
                    : _iconService.DefaultFileIcon;

                var fileResource = new FileResource(childName, folderResource, iconDefinition);
                fileResource.WritableState = writableState;
                folderResource.AddChild(fileResource);
            }
        }

        // EnumerateFolderAsync yields folders-first, ordinal order, which matches
        // the tree's required ordering, so no re-sort is needed here.
        return Result.Ok();
    }
}
