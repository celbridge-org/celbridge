using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Resources.Services;

/// <summary>
/// Walks a folder tree one listing at a time through the file-system gateway.
/// </summary>
public sealed class ResourceWalker : IResourceWalker
{
    // Records the state of one walk: the gateway and policy for each listing, the search list option, the filter
    // and the items found so far.
    private sealed record Walk(
        IResourceFileSystem FileSystem,
        IResourcePolicy Policy,
        bool ApplySearchExclude,
        IPathMatcher? Filter,
        CancellationToken CancellationToken)
    {
        public List<WalkedItem> Items { get; } = new();
    }

    private readonly ILogger<ResourceWalker> _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceWalker(
        ILogger<ResourceWalker> logger,
        IWorkspaceWrapper workspaceWrapper)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
    }

    public async Task<Result<IReadOnlyList<WalkedItem>>> WalkAsync(
        ResourceKey folder,
        bool applySearchExclude = true,
        IPathMatcher? filter = null,
        CancellationToken cancellationToken = default)
    {
        // The gateway lists a folder synchronously, so the whole walk runs on a worker thread.
        return await Task.Run(
            () => WalkFolderAsync(folder, applySearchExclude, filter, cancellationToken),
            cancellationToken);
    }

    private async Task<Result<IReadOnlyList<WalkedItem>>> WalkFolderAsync(
        ResourceKey folder,
        bool applySearchExclude,
        IPathMatcher? filter,
        CancellationToken cancellationToken)
    {
        var resourceService = _workspaceWrapper.WorkspaceService.ResourceService;

        // Every key the walk returns starts with the folder's key, so a folder key that differs from the folder's
        // stored name fails here. Each item's path joins the folder's path with the stored name from the listing.
        var resolveResult = resourceService.RootHandlers.ResolveResourcePath(folder);
        if (resolveResult.IsFailure)
        {
            return Result.Fail($"Failed to resolve the folder to walk: '{folder}'")
                .WithErrors(resolveResult);
        }
        var folderPath = resolveResult.Value;

        var listResult = await resourceService.FileSystem.EnumerateFolderAsync(folder);
        if (listResult.IsFailure)
        {
            return Result.Fail($"Failed to list the folder to walk: '{folder}'")
                .WithErrors(listResult);
        }
        var folderChildren = listResult.Value;

        // The search list matches each folder on a path, so the list leaves out everything below an excluded folder.
        // A walk that starts in an excluded folder, or in a folder inside one, lists the folder in full.
        var startsInExcludedFolder = resourceService.Policy.IsSearchExcluded(folder, isFolder: true);
        var walkApplySearchExclude = applySearchExclude
            && !startsInExcludedFolder;

        var walk = new Walk(
            resourceService.FileSystem,
            resourceService.Policy,
            walkApplySearchExclude,
            filter,
            cancellationToken);
        await AddFolderContentsAsync(walk, folderChildren, folderPath);

        IReadOnlyList<WalkedItem> items = walk.Items;
        return items.OkResult();
    }

    private async Task AddFolderContentsAsync(Walk walk, IReadOnlyList<FolderChild> folderChildren, string folderPath)
    {
        foreach (var folderChild in folderChildren)
        {
            walk.CancellationToken.ThrowIfCancellationRequested();

            if (walk.ApplySearchExclude
                && walk.Policy.IsSearchExcluded(folderChild.Resource, folderChild.IsFolder))
            {
                continue;
            }

            var itemPath = Path.Combine(folderPath, folderChild.Resource.ResourceName);
            var walkedItem = new WalkedItem(
                folderChild.Resource,
                itemPath,
                folderChild.IsFolder,
                folderChild.Size,
                folderChild.ModifiedUtc,
                folderChild.Attributes);
            walk.Items.Add(walkedItem);

            if (!folderChild.IsFolder)
            {
                continue;
            }

            if (walk.Filter is not null
                && !walk.Filter.CanMatchInside(folderChild.Resource.Path))
            {
                continue;
            }

            // A folder can go between its parent's listing and its own. The walk goes on without it.
            var listResult = await walk.FileSystem.EnumerateFolderAsync(folderChild.Resource);
            if (listResult.IsFailure)
            {
                _logger.LogDebug(
                    $"Walk skipped a folder it could not list: '{folderChild.Resource}' ({listResult.FirstErrorMessage})");
                continue;
            }

            var childItems = listResult.Value;

            await AddFolderContentsAsync(walk, childItems, itemPath);
        }
    }
}
