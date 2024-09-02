using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Resources.Commands;

public class SelectResourceCommand : CommandBase, ISelectResourceCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey Resource { get; set; }

    public SelectResourceCommand(
        IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var resourceService = _workspaceWrapper.WorkspaceService.ResourceService;

        var selectResult = resourceService.SetSelectedResource(Resource);
        if (selectResult.IsFailure)
        {
            return selectResult;
        }

        await Task.CompletedTask;
        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //
    public static void SelectResource(ResourceKey Resource)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<ISelectResourceCommand>(command =>
        {
            command.Resource = Resource;
        });
    }
}
