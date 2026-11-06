namespace Celbridge.Resources.Helpers;

/// <summary>
/// Summarizes a file or the files beneath a folder: the number of files, their total size and the newest modified
/// time. Editing, adding or removing a file normally changes the stamp.
/// </summary>
internal sealed record ItemStamp(int FileCount, long TotalSize, DateTime NewestModifiedUtc)
{
    public static ItemStamp Empty { get; } = new(0, 0, DateTime.MinValue);

    public ItemStamp AddFile(long size, DateTime modifiedUtc)
    {
        var newestModifiedUtc = modifiedUtc > NewestModifiedUtc
            ? modifiedUtc
            : NewestModifiedUtc;

        return new ItemStamp(FileCount + 1, TotalSize + size, newestModifiedUtc);
    }

    /// <summary>
    /// Returns true when a stamp was recorded and the current stamp equals it.
    /// </summary>
    public static bool Matches(ItemStamp? recordedStamp, ItemStamp? currentStamp)
    {
        return recordedStamp is not null
            && recordedStamp == currentStamp;
    }
}

/// <summary>
/// Describes how a resource compares with a recorded stamp.
/// </summary>
internal enum ItemStampComparison
{
    /// <summary>
    /// No item is at the resource's key.
    /// </summary>
    Absent,

    /// <summary>
    /// The item's stamp equals the recorded stamp.
    /// </summary>
    Unchanged,

    /// <summary>
    /// The item's stamp differs from the recorded stamp, or one of the two stamps is missing.
    /// </summary>
    Changed,
}

/// <summary>
/// Reads the stamps of files and folders, and compares a resource with a recorded stamp.
/// </summary>
internal sealed class ItemStampReader
{
    private readonly IResourceFileSystem _resourceFileSystem;
    private readonly IResourceWalker _resourceWalker;

    public ItemStampReader(IResourceFileSystem resourceFileSystem, IResourceWalker resourceWalker)
    {
        _resourceFileSystem = resourceFileSystem;
        _resourceWalker = resourceWalker;
    }

    /// <summary>
    /// Returns the stamp of a resource, or null when the resource is absent or can't be read. A folder's stamp covers
    /// the files beneath the folder. The walker lists those files.
    /// </summary>
    public async Task<ItemStamp?> ReadAsync(ResourceKey resource)
    {
        var infoResult = await _resourceFileSystem.GetInfoAsync(resource);
        if (infoResult.IsFailure)
        {
            return null;
        }
        var info = infoResult.Value;

        if (info.Kind == StorageItemKind.File)
        {
            return ItemStamp.Empty.AddFile(info.Size, info.ModifiedUtc);
        }

        if (info.Kind != StorageItemKind.Folder)
        {
            return null;
        }

        var walkResult = await _resourceWalker.WalkAsync(resource, applySearchExclude: false);
        if (walkResult.IsFailure)
        {
            return null;
        }

        var stamp = ItemStamp.Empty;
        foreach (var item in walkResult.Value)
        {
            if (!item.IsFolder)
            {
                stamp = stamp.AddFile(item.Size, item.ModifiedUtc);
            }
        }

        return stamp;
    }

    /// <summary>
    /// Returns the stamp of a file or folder outside every root. Returns null when the path is absent, can't be read,
    /// is a link or is the other kind of item. A folder's stamp leaves out links.
    /// </summary>
    public static async Task<ItemStamp?> ReadExternalAsync(string path, bool isFolder, ILocalFileSystem fileSystem)
    {
        var infoResult = await fileSystem.GetInfoAsync(path);
        if (infoResult.IsFailure)
        {
            return null;
        }
        var info = infoResult.Value;

        var expectedKind = isFolder
            ? StorageItemKind.Folder
            : StorageItemKind.File;
        if (info.IsLink
            || info.Kind != expectedKind)
        {
            return null;
        }

        if (!isFolder)
        {
            return ItemStamp.Empty.AddFile(info.Size, info.ModifiedUtc);
        }

        // The local file system lists a folder on the calling thread, so a worker thread does the listing.
        var enumerateResult = await Task.Run(() => fileSystem.EnumerateAsync(path, "*", recursive: true));
        if (enumerateResult.IsFailure)
        {
            return null;
        }

        var stamp = ItemStamp.Empty;
        foreach (var entry in enumerateResult.Value)
        {
            if (!entry.IsFolder
                && !entry.IsLink)
            {
                stamp = stamp.AddFile(entry.Size, entry.ModifiedUtc);
            }
        }

        return stamp;
    }

    /// <summary>
    /// Compares a resource with a recorded stamp. Only NotFound counts as absent. A resource that can't be read counts
    /// as changed, and so does a missing recorded stamp.
    /// </summary>
    public async Task<ItemStampComparison> CompareAsync(ResourceKey resource, ItemStamp? recordedStamp)
    {
        var infoResult = await _resourceFileSystem.GetInfoAsync(resource);
        if (infoResult.IsSuccess
            && infoResult.Value.Kind == StorageItemKind.NotFound)
        {
            return ItemStampComparison.Absent;
        }

        var currentStamp = await ReadAsync(resource);

        return ItemStamp.Matches(recordedStamp, currentStamp)
            ? ItemStampComparison.Unchanged
            : ItemStampComparison.Changed;
    }
}
