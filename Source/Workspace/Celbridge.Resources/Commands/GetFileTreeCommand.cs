using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

public class GetFileTreeCommand : CommandBase, IGetFileTreeCommand
{
    // Records the settings of one tree walk. KeepEveryFolder is true when the walk has no glob and the type filter
    // allows folders. Otherwise the walk keeps only the folders that may contain a match.
    private sealed record Walk(
        IResourceFileSystem FileSystem,
        SearchFilter? Filter,
        bool ShowFiles,
        bool KeepEveryFolder);

    // Records a folder's node in the snapshot. MayContainMatch is true when a file below the folder passes the glob,
    // or when the depth limit hides part of the folder. With no glob, every file passes.
    private sealed record FolderSnapshot(FileTreeSnapshotNode Node, bool MayContainMatch);

    private readonly IWorkspaceWrapper _workspaceWrapper;

    public override CommandFlags CommandFlags => CommandFlags.SuppressCommandLog;

    public ResourceKey Resource { get; set; }
    public int Depth { get; set; } = 3;
    public string Glob { get; set; } = string.Empty;
    public string TypeFilter { get; set; } = string.Empty;

    public FileTreeSnapshot ResultValue { get; private set; }
        = new FileTreeSnapshot(new FileTreeSnapshotNode(
            string.Empty,
            IsFolder: true,
            Children: Array.Empty<FileTreeSnapshotNode>(),
            Truncated: false));

    public GetFileTreeCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;

        // A starting folder that is missing, or is not a folder, fails the command. The walk skips unreadable
        // subfolders.
        var rootEntriesResult = await resourceFileSystem.EnumerateFolderAsync(Resource);
        if (rootEntriesResult.IsFailure)
        {
            return Result.Fail($"Resource not found: '{Resource}'")
                .WithErrors(rootEntriesResult);
        }
        var rootEntries = rootEntriesResult.Value;

        SearchFilter? filter = null;
        if (!string.IsNullOrEmpty(Glob))
        {
            var filterResult = SearchFilter.Compile(Glob, string.Empty);
            if (filterResult.IsFailure)
            {
                return Result.Fail(filterResult.FirstErrorMessage);
            }
            filter = filterResult.Value;
        }

        var isFileType = string.Equals(TypeFilter, "file", StringComparison.OrdinalIgnoreCase);
        var isFolderType = string.Equals(TypeFilter, "folder", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(TypeFilter)
            && !isFileType
            && !isFolderType)
        {
            return Result.Fail($"Invalid type '{TypeFilter}': must be \"file\" or \"folder\", or empty for both.");
        }

        var showFiles = !isFolderType;
        var showFolders = !isFileType;
        var keepEveryFolder = filter is null
            && showFolders;
        var walk = new Walk(resourceFileSystem, filter, showFiles, keepEveryFolder);

        var folderName = Resource.IsEmpty
            ? Resource.Root
            : Resource.ResourceName;

        // The gateway lists a folder synchronously, so the whole walk runs on a worker thread.
        var rootSnapshot = await Task.Run(() => BuildFolderSnapshotAsync(walk, folderName, rootEntries, Depth));
        ResultValue = new FileTreeSnapshot(rootSnapshot.Node);

        return Result.Ok();
    }

    // Builds the snapshot of the folder with the supplied entries. A subfolder stays in the tree when the walk keeps
    // every folder, or when a match may be below the subfolder. The walk skips a subfolder when the glob cannot match
    // inside it. Subfolder enumeration failures are swallowed so a single unreadable folder doesn't break the whole
    // tree.
    private static async Task<FolderSnapshot> BuildFolderSnapshotAsync(
        Walk walk,
        string folderName,
        IReadOnlyList<FolderChild> entries,
        int remainingDepth)
    {
        if (remainingDepth <= 0)
        {
            var isTruncated = entries.Count > 0;
            var truncatedNode = new FileTreeSnapshotNode(
                folderName,
                IsFolder: true,
                Children: Array.Empty<FileTreeSnapshotNode>(),
                Truncated: isTruncated);

            return new FolderSnapshot(truncatedNode, MayContainMatch: isTruncated);
        }

        var children = new List<FileTreeSnapshotNode>();
        var mayContainMatch = false;

        foreach (var entry in entries)
        {
            if (entry.IsFolder)
            {
                if (walk.Filter is not null
                    && !walk.Filter.CanMatchInside(entry.Resource.Path))
                {
                    continue;
                }

                var childEntriesResult = await walk.FileSystem.EnumerateFolderAsync(entry.Resource);
                if (childEntriesResult.IsFailure)
                {
                    continue;
                }
                var childEntries = childEntriesResult.Value;

                var childSnapshot = await BuildFolderSnapshotAsync(
                    walk, entry.Resource.ResourceName, childEntries, remainingDepth - 1);
                if (childSnapshot.MayContainMatch)
                {
                    mayContainMatch = true;
                }

                if (walk.KeepEveryFolder
                    || childSnapshot.MayContainMatch)
                {
                    children.Add(childSnapshot.Node);
                }
            }
            else
            {
                if (walk.Filter is not null
                    && !walk.Filter.IsMatch(entry.Resource.Path, isFolder: false))
                {
                    continue;
                }

                mayContainMatch = true;

                if (walk.ShowFiles)
                {
                    children.Add(new FileTreeSnapshotNode(
                        entry.Resource.ResourceName,
                        IsFolder: false,
                        Children: Array.Empty<FileTreeSnapshotNode>(),
                        Truncated: false));
                }
            }
        }

        var node = new FileTreeSnapshotNode(
            folderName,
            IsFolder: true,
            Children: children,
            Truncated: false);

        return new FolderSnapshot(node, mayContainMatch);
    }
}
