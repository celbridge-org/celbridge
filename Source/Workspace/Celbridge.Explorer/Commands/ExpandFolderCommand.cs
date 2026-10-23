using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Explorer.Commands;

public class ExpandFolderCommand : CommandBase, IExpandFolderCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    public override CommandFlags CommandFlags => CommandFlags.SaveWorkspaceState;

    public ResourceKey FolderResource { get; set; }
    public bool Expanded { get; set; } = true;

    public ExpandFolderCommand(
        IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;

        var getResult = await resourceFileSystem.GetResourceItemAsync(FolderResource);
        if (getResult.IsFailure)
        {
            return Result.Fail($"Folder resource not found: '{FolderResource}'")
                .WithErrors(getResult);
        }

        var resourceItem = getResult.Value;
        if (!resourceItem.IsFolder)
        {
            return Result.Fail($"Resource is not a folder. {FolderResource}");
        }

        var explorerPanel = _workspaceWrapper.WorkspaceService.UtilityPanel.ExplorerPanel;
        await explorerPanel.SetFolderExpandedAsync(FolderResource, Expanded);

        return Result.Ok();
    }
}
