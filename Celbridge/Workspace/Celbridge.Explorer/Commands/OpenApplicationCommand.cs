using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Explorer.Commands;

public class OpenApplicationCommand : CommandBase, IOpenApplicationCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey Resource { get; set; }

    public OpenApplicationCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var explorerService = _workspaceWrapper.WorkspaceService.ExplorerService;

        var openResult = await explorerService.OpenApplication(Resource);
        if (openResult.IsFailure)
        {
            return openResult;
        }

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //

    public static void OpenApplication(ResourceKey resource)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

        commandService.Execute<IOpenApplicationCommand>(command =>
        {
            command.Resource = resource;
        });
    }
}
