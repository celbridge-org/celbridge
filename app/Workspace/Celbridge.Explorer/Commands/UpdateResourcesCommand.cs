using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Explorer.Commands;

public class UpdateResourcesCommand : CommandBase, IUpdateResourcesCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public UpdateResourcesCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var resourceService = _workspaceWrapper.WorkspaceService.ResourceService;
        resourceService.ScheduleResourceUpdate();

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //
    public static void UpdateResourceRegistry()
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IUpdateResourcesCommand>();
    }
}
