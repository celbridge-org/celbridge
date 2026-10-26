namespace Celbridge.Resources;

/// <summary>
/// A file or folder that a walk found, with its absolute path and the details its folder's listing gave.
/// </summary>
public record WalkedItem(
    ResourceKey Resource,
    string Path,
    bool IsFolder,
    long Size,
    DateTime ModifiedUtc,
    FileSystemAttributes Attributes);

/// <summary>
/// Walks a folder and every folder below it through the file-system gateway.
/// </summary>
public interface IResourceWalker
{
    /// <summary>
    /// Returns every file and folder below the folder, each folder before its contents. The walk runs off the
    /// calling thread, and leaves out links and .git. In a project: folder the walk also leaves out each item
    /// matched by the search list, and everything inside it, unless applySearchExclude is false. A walk that starts
    /// in an excluded folder, or in a folder inside one, lists the whole folder. Fails when the folder cannot be
    /// listed, as for a key that differs from the folder's stored name. A cancelled walk throws
    /// OperationCanceledException.
    /// </summary>
    Task<Result<IReadOnlyList<WalkedItem>>> WalkAsync(
        ResourceKey folder,
        bool applySearchExclude = true,
        CancellationToken cancellationToken = default);
}
