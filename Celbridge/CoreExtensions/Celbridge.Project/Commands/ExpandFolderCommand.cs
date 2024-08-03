using Celbridge.Commands;
using Celbridge.Resources;
using Celbridge.Workspace;
using CommunityToolkit.Diagnostics;

namespace Celbridge.Projects.Commands;

public class ExpandFolderCommand : CommandBase, IExpandFolderCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    public override CommandFlags CommandFlags => CommandFlags.SaveWorkspaceState; 

    public ResourceKey FolderResource { get; set; }
    public bool IsExpanded { get; set; } = true;

    public ExpandFolderCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var resourceRegistry = _workspaceWrapper.WorkspaceService.ProjectService.ResourceRegistry;
        Guard.IsNotNull(resourceRegistry);

        resourceRegistry.SetFolderIsExpanded(FolderResource, IsExpanded);

        await Task.CompletedTask;

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //
    public static void ExpandFolder(ResourceKey folderResource, bool IsExpanded)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IExpandFolderCommand>(command =>
        {
            command.FolderResource = folderResource;
            command.IsExpanded = IsExpanded;
        });
    }
}
