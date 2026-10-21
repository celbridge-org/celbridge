using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.Resources.Services;

/// <summary>
/// Walks a folder tree one listing at a time through the file-system gateway.
/// </summary>
public sealed class ResourceWalker : IResourceWalker
{
    // Records the state of one walk: the gateway and policy for each listing, the search-exclude option and the items
    // found so far.
    private sealed record Walk(
        IResourceFileSystem FileSystem,
        IResourcePolicy Policy,
        bool ApplySearchExclude,
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
        CancellationToken cancellationToken = default)
    {
        // The gateway lists a folder synchronously, so the whole walk runs on a worker thread.
        return await Task.Run(() => WalkFolderAsync(folder, applySearchExclude, cancellationToken), cancellationToken);
    }

    private async Task<Result<IReadOnlyList<WalkedItem>>> WalkFolderAsync(
        ResourceKey folder,
        bool applySearchExclude,
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
        var folderItems = listResult.Value;

        var walk = new Walk(resourceService.FileSystem, resourceService.Policy, applySearchExclude, cancellationToken);
        await AddFolderContentsAsync(walk, folderItems, folderPath);

        IReadOnlyList<WalkedItem> items = walk.Items;
        return items.OkResult();
    }

    private async Task AddFolderContentsAsync(Walk walk, IReadOnlyList<FolderItem> folderItems, string folderPath)
    {
        foreach (var folderItem in folderItems)
        {
            walk.CancellationToken.ThrowIfCancellationRequested();

            if (walk.ApplySearchExclude
                && walk.Policy.IsSearchExcluded(folderItem.Resource, folderItem.IsFolder))
            {
                continue;
            }

            var itemPath = Path.Combine(folderPath, folderItem.Resource.ResourceName);
            var walkedItem = new WalkedItem(
                folderItem.Resource,
                itemPath,
                folderItem.IsFolder,
                folderItem.Size,
                folderItem.ModifiedUtc,
                folderItem.Attributes);
            walk.Items.Add(walkedItem);

            if (!folderItem.IsFolder)
            {
                continue;
            }

            // A folder can go between its parent's listing and its own. The walk goes on without it.
            var listResult = await walk.FileSystem.EnumerateFolderAsync(folderItem.Resource);
            if (listResult.IsFailure)
            {
                _logger.LogDebug(
                    $"Walk skipped a folder it could not list: '{folderItem.Resource}' ({listResult.FirstErrorMessage})");
                continue;
            }

            var childItems = listResult.Value;

            await AddFolderContentsAsync(walk, childItems, itemPath);
        }
    }
}
