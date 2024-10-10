using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Explorer.Commands;

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
        var explorerService = _workspaceWrapper.WorkspaceService.ExplorerService;

        var updateResult = await explorerService.UpdateResourcesAsync();
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
