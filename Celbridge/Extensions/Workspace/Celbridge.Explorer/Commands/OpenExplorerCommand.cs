using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Explorer.Commands;

public class OpenExplorerCommand : CommandBase, IOpenExplorerCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey Resource { get; set; }

    public OpenExplorerCommand(
        IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var explorerService = _workspaceWrapper.WorkspaceService.ExplorerService;

        var openResult = await explorerService.OpenFileManager(Resource);
        if (openResult.IsFailure)
        {
            return openResult;
        }

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //
    public static void OpenExplorer(ResourceKey resource)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IOpenExplorerCommand>(command =>
        {
            command.Resource = resource;
        });
    }
}
