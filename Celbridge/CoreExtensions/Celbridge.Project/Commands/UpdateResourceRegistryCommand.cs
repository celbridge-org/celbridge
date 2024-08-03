using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Projects.Commands;

public class UpdateResourceRegistryCommand : CommandBase, IUpdateResourceRegistryCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public UpdateResourceRegistryCommand(
        IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var projectService = _workspaceWrapper.WorkspaceService.ProjectService;

        var updateResult = await projectService.UpdateResourcesAsync();
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
        commandService.Execute<IUpdateResourceRegistryCommand>();
    }
}
