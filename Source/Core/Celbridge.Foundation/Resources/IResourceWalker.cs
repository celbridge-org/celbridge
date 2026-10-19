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
    /// calling thread, and leaves out links and .git. In a project: folder the walk also leaves out each item that
    /// search-exclude matches, and everything inside it, unless applySearchExclude is false. Fails when the folder
    /// cannot be listed. A cancelled walk throws OperationCanceledException.
    /// </summary>
    Task<Result<IReadOnlyList<WalkedItem>>> WalkAsync(
        ResourceKey folder,
        bool applySearchExclude = true,
        CancellationToken cancellationToken = default);
}
