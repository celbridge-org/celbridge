using System.Text.RegularExpressions;
using Celbridge.Commands;
using Celbridge.Utilities;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

public class GetFileTreeCommand : CommandBase, IGetFileTreeCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public override CommandFlags CommandFlags => CommandFlags.SuppressCommandLog;

    public ResourceKey Resource { get; set; }
    public int Depth { get; set; } = 3;
    public string Glob { get; set; } = string.Empty;
    public string TypeFilter { get; set; } = string.Empty;

    public FileTreeSnapshot ResultValue { get; private set; }
        = new FileTreeSnapshot(null);

    public GetFileTreeCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;

        // EnumerateFolderAsync at the root surfaces a missing-or-not-a-folder
        // error to the caller; deeper recursion silently skips unreadable
        // subfolders to match the existing tree-walk behavior.
        var rootEntriesResult = await resourceFileSystem.EnumerateFolderAsync(Resource);
        if (rootEntriesResult.IsFailure)
        {
            return Result.Fail($"Resource not found: '{Resource}'")
                .WithErrors(rootEntriesResult);
        }
        var rootEntries = rootEntriesResult.Value;

        Regex? globRegex = null;
        if (!string.IsNullOrEmpty(Glob))
        {
            globRegex = new Regex(GlobHelper.GlobToRegex(Glob), RegexOptions.IgnoreCase);
        }

        var folderName = Resource.IsEmpty
            ? Resource.Root
            : Resource.ResourceName;

        var rootNode = await BuildSnapshotAsync(
            resourceFileSystem, folderName, rootEntries, Depth, globRegex, TypeFilter);
        ResultValue = new FileTreeSnapshot(rootNode);

        return Result.Ok();
    }

    // Returns the snapshot node for the folder with the supplied entries, or
    // null when a non-empty glob filters every child out and the folder itself
    // is therefore irrelevant to the result. Subfolder enumeration failures are
    // swallowed so a single unreadable directory doesn't break the whole tree.
    private static async Task<FileTreeSnapshotNode?> BuildSnapshotAsync(
        IResourceFileSystem resourceFileSystem,
        string folderName,
        IReadOnlyList<FolderItem> entries,
        int remainingDepth,
        Regex? globRegex,
        string typeFilter)
    {
        var children = new List<FileTreeSnapshotNode>();
        bool isTruncated = false;

        if (remainingDepth > 0)
        {
            var showFiles = !string.Equals(typeFilter, "folder", StringComparison.OrdinalIgnoreCase);
            var showFolders = !string.Equals(typeFilter, "file", StringComparison.OrdinalIgnoreCase);

            foreach (var entry in entries)
            {
                if (entry.IsFolder)
                {
                    var childEntriesResult = await resourceFileSystem.EnumerateFolderAsync(entry.Resource);
                    if (childEntriesResult.IsFailure)
                    {
                        continue;
                    }

                    var childNode = await BuildSnapshotAsync(
                        resourceFileSystem, entry.Resource.ResourceName, childEntriesResult.Value,
                        remainingDepth - 1, globRegex, typeFilter);
                    if (childNode is not null && showFolders)
                    {
                        children.Add(childNode);
                    }
                }
                else if (showFiles)
                {
                    var fileName = entry.Resource.ResourceName;
                    if (globRegex is not null && !globRegex.IsMatch(fileName))
                    {
                        continue;
                    }
                    children.Add(new FileTreeSnapshotNode(
                        fileName,
                        IsFolder: false,
                        Children: Array.Empty<FileTreeSnapshotNode>(),
                        Truncated: false));
                }
            }
        }
        else if (entries.Count > 0)
        {
            isTruncated = true;
        }

        if (globRegex is not null && !isTruncated && children.Count == 0 && remainingDepth > 0)
        {
            return null;
        }

        return new FileTreeSnapshotNode(
            folderName,
            IsFolder: true,
            Children: children,
            Truncated: isTruncated);
    }
}
