using Celbridge.Resources.Services.Roots;

namespace Celbridge.Resources.Services;

/// <summary>
/// Checks what is at each key of one batch of changes in a root: a file, a folder or nothing. Each folder is listed at
/// most once for the batch.
/// </summary>
internal sealed class BatchItemChecker
{
    private readonly Func<ResourceKey, Result<string>> _resolve;
    private readonly ILocalFileSystem _fileSystem;

    /// <summary>
    /// Creates a checker for a batch. Create it only after every change in the batch has been recorded with the root's
    /// resolver. A checker created earlier could accept a folder listing that misses one of those changes.
    /// </summary>
    public BatchItemChecker(IResourceRootHandler handler, ILocalFileSystem fileSystem)
    {
        // A root handler that does not derive from ResourceRootHandlerBase resolves each key separately. A folder may
        // then be listed more than once for the batch.
        _resolve = handler is ResourceRootHandlerBase root
            ? root.CreateBatchResolver()
            : handler.Resolve;
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Returns what is at the key. An item exists only when its key resolves. The resolver refuses a link, and a key
    /// that differs from the name stored on disk.
    /// </summary>
    public async Task<ResourceItemState> CheckAsync(ResourceKey resource)
    {
        var resolveResult = _resolve(resource);
        if (resolveResult.IsFailure)
        {
            return ResourceItemState.Absent;
        }
        var path = resolveResult.Value;

        var infoResult = await _fileSystem.GetInfoAsync(path);
        if (infoResult.IsFailure)
        {
            return ResourceItemState.Absent;
        }

        return infoResult.Value.Kind switch
        {
            StorageItemKind.File => ResourceItemState.File,
            StorageItemKind.Folder => ResourceItemState.Folder,
            _ => ResourceItemState.Absent,
        };
    }
}
