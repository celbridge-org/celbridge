using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Explorer.Commands;

public class ExpandFolderCommand : CommandBase, IExpandFolderCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    public override CommandFlags CommandFlags => CommandFlags.RefreshResourceTree | CommandFlags.SaveWorkspaceState;

    public ResourceKey FolderResource { get; set; }
    public bool Expanded { get; set; } = true;

    public ExpandFolderCommand(
        IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        Guard.IsNotNull(resourceRegistry);

        var getResult = resourceRegistry.GetResource(FolderResource);
        if (getResult.IsFailure)
        {
            return Result.Fail($"Folder resource not found: '{FolderResource}'")
                .WithErrors(getResult);
        }

        if (getResult.Value is not IFolderResource)
        {
            return Result.Fail($"Resource is not a folder. {FolderResource}");
        }

        var folderStateService = _workspaceWrapper.WorkspaceService.ExplorerService.FolderStateService;

        if (folderStateService.IsExpanded(FolderResource) != Expanded)
        {
            folderStateService.SetExpanded(FolderResource, Expanded);
        }

        await Task.CompletedTask;

        return Result.Ok();
    }
}
