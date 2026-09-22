using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

public class UpdateResourcesCommand : CommandBase, IUpdateResourcesCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public bool Immediate { get; set; }

    public UpdateResourcesCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var resourceService = _workspaceWrapper.WorkspaceService.ResourceService;

        if (Immediate)
        {
            return await resourceService.UpdateResourcesAsync();
        }

        resourceService.ScheduleResourceUpdate();

        return Result.Ok();
    }
}
