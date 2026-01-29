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
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ResourceService.Registry;
        Guard.IsNotNull(resourceRegistry);

        var getResult = resourceRegistry.GetResource(FolderResource);
        if (getResult.IsFailure)
        {
            return Result.Fail($"Folder resource not found. {FolderResource}");
        }

        var folderResource = getResult.Value as IFolderResource;
        if (folderResource is null)
        {
            return Result.Fail($"Resource is not a folder. {FolderResource}");
        }

        var folderStateService = _workspaceWrapper.WorkspaceService.ExplorerService.FolderStateService;

        if (folderStateService.IsExpanded(FolderResource) != Expanded)
        {
            folderStateService.SetExpanded(FolderResource, Expanded);
        }

        if (folderResource.IsExpanded != Expanded)
        {
            folderResource.IsExpanded = Expanded;
        }

        await Task.CompletedTask;

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //
    public static void ExpandFolder(ResourceKey folderResource, bool IsExpanded)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IExpandFolderCommand>(command =>
        {
            command.FolderResource = folderResource;
            command.Expanded = IsExpanded;
        });
    }
}
