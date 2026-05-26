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
        var fileSystem = _workspaceWrapper.WorkspaceService.ResourceFileSystem;

        var enumerateResult = await fileSystem.EnumerateFolderAsync(Resource);
        if (enumerateResult.IsFailure)
        {
            return Result.Fail($"Resource not found: '{Resource}'")
                .WithErrors(enumerateResult);
        }

        var entries = enumerateResult.Value
            .Select(entry => new FolderContentsEntry(
                entry.Resource.ResourceName,
                IsFolder: entry.IsFolder,
                Size: entry.Size,
                ModifiedUtc: entry.ModifiedUtc))
            .ToList();

        ResultValue = new FolderContentsSnapshot(entries);

        return Result.Ok();
    }
}
