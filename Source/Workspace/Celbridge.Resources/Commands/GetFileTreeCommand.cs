using System.Text.RegularExpressions;
using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

public class GetFileTreeCommand : CommandBase, IGetFileTreeCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public override CommandFlags CommandFlags => CommandFlags.Query;

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

    public override Task<Result> ExecuteAsync()
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var getResult = resourceRegistry.GetResource(Resource);
        if (getResult.IsFailure)
        {
            return Task.FromResult<Result>(Result.Fail($"Resource not found: '{Resource}'"));
        }

        if (getResult.Value is not IFolderResource folderResource)
        {
            return Task.FromResult<Result>(Result.Fail($"Resource is not a folder: '{Resource}'"));
        }

        Regex? globRegex = null;
        if (!string.IsNullOrEmpty(Glob))
        {
            // Inlined from GlobHelper.GlobToRegex. Resources does not depend on Utilities,
            // so the conversion is duplicated here rather than pulling in the extra reference.
            var escaped = Regex.Escape(Glob)
                .Replace("\\*", ".*")
                .Replace("\\?", ".");
            globRegex = new Regex($"^{escaped}$", RegexOptions.IgnoreCase);
        }

        var rootNode = BuildSnapshot(folderResource, Depth, globRegex, TypeFilter);
        ResultValue = new FileTreeSnapshot(rootNode);

        return Task.FromResult(Result.Ok());
    }

    private static FileTreeSnapshotNode? BuildSnapshot(
        IFolderResource folder,
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

            foreach (var child in folder.Children)
            {
                if (child is IFolderResource childFolder)
                {
                    var childNode = BuildSnapshot(childFolder, remainingDepth - 1, globRegex, typeFilter);
                    if (childNode is not null && showFolders)
                    {
                        children.Add(childNode);
                    }
                }
                else if (showFiles)
                {
                    if (globRegex is not null && !globRegex.IsMatch(child.Name))
                    {
                        continue;
                    }
                    children.Add(new FileTreeSnapshotNode(
                        child.Name,
                        IsFolder: false,
                        Children: Array.Empty<FileTreeSnapshotNode>(),
                        Truncated: false));
                }
            }
        }
        else if (folder.Children.Any())
        {
            isTruncated = true;
        }

        if (globRegex is not null && !isTruncated && children.Count == 0 && remainingDepth > 0)
        {
            return null;
        }

        return new FileTreeSnapshotNode(
            folder.Name,
            IsFolder: true,
            Children: children,
            Truncated: isTruncated);
    }
}
