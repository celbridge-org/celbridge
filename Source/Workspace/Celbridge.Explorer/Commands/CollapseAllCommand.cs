using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Explorer.Commands;

public class CollapseAllCommand : CommandBase, ICollapseAllCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    public override CommandFlags CommandFlags => CommandFlags.RefreshResourceTree | CommandFlags.SaveWorkspaceState;

    public CollapseAllCommand(
        IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        Guard.IsNotNull(resourceRegistry);

        var folderStateService = _workspaceWrapper.WorkspaceService.ExplorerService.FolderStateService;

        CollapseAllFolders(resourceRegistry.RootFolder, resourceRegistry, folderStateService);

        await Task.CompletedTask;

        return Result.Ok();
    }

    private static void CollapseAllFolders(
        IFolderResource folder,
        IResourceRegistry resourceRegistry,
        IFolderStateService folderStateService)
    {
        foreach (var child in folder.Children)
        {
            if (child is IFolderResource childFolder)
            {
                var folderKey = resourceRegistry.GetResourceKey(childFolder);

                if (childFolder.IsExpanded)
                {
                    childFolder.IsExpanded = false;
                }

                if (folderStateService.IsExpanded(folderKey))
                {
                    folderStateService.SetExpanded(folderKey, false);
                }

                CollapseAllFolders(childFolder, resourceRegistry, folderStateService);
            }
        }
    }
}
