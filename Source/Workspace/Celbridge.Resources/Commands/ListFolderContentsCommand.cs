using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

public class ListFolderContentsCommand : CommandBase, IListFolderContentsCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public override CommandFlags CommandFlags => CommandFlags.SuppressCommandLog;

    public ResourceKey Resource { get; set; }

    public FolderContentsSnapshot ResultValue { get; private set; }
        = new FolderContentsSnapshot(Array.Empty<FolderContentsEntry>());

    public ListFolderContentsCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        await Task.CompletedTask;

        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;

        var getResult = resourceRegistry.GetResource(Resource);
        if (getResult.IsFailure)
        {
            return Result.Fail($"Resource not found: '{Resource}'");
        }

        if (getResult.Value is not IFolderResource folderResource)
        {
            return Result.Fail($"Resource is not a folder: '{Resource}'");
        }

        var entries = new List<FolderContentsEntry>();
        foreach (var child in folderResource.Children)
        {
            var childKey = resourceRegistry.GetResourceKey(child);
            var resolveResult = resourceRegistry.ResolveResourcePath(childKey);
            if (resolveResult.IsFailure)
            {
                continue;
            }
            var childPath = resolveResult.Value;

            if (child is IFolderResource)
            {
                var directoryInfo = new DirectoryInfo(childPath);
                entries.Add(new FolderContentsEntry(
                    child.Name,
                    IsFolder: true,
                    Size: 0,
                    ModifiedUtc: directoryInfo.LastWriteTimeUtc));
            }
            else
            {
                var fileInfo = new FileInfo(childPath);
                entries.Add(new FolderContentsEntry(
                    child.Name,
                    IsFolder: false,
                    Size: fileInfo.Length,
                    ModifiedUtc: fileInfo.LastWriteTimeUtc));
            }
        }

        ResultValue = new FolderContentsSnapshot(entries);

        return Result.Ok();
    }
}
