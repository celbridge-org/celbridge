using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Explorer.Commands;

public class OpenFileManagerCommand : CommandBase, IOpenFileManagerCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey Resource { get; set; }

    public OpenFileManagerCommand(
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

    public static void OpenFileManager(ResourceKey resource)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<IOpenFileManagerCommand>(command =>
        {
            command.Resource = resource;
        });
    }
}
