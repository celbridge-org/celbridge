using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

public class UpdateResourcesCommand : CommandBase, IUpdateResourcesCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public UpdateResourcesCommand(
        IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var resourceService = _workspaceWrapper.WorkspaceService.ResourceService;

        var updateResult = await resourceService.UpdateResourcesAsync();
        if (updateResult.IsFailure)
        {
            return updateResult;
        }

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //
    public static void UpdateResourceRegistry()
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IUpdateResourcesCommand>();
    }
}
