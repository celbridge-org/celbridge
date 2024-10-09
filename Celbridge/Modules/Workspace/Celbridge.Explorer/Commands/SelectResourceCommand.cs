using Celbridge.Commands;
using Celbridge.Foundation;
using Celbridge.Workspace;

namespace Celbridge.Explorer.Commands;

public class SelectResourceCommand : CommandBase, ISelectResourceCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey Resource { get; set; }

    public bool ShowExplorerPanel { get; set; } = true;

    public SelectResourceCommand(
        IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var explorerService = _workspaceWrapper.WorkspaceService.ExplorerService;

        var selectResult = await explorerService.SelectResource(Resource, ShowExplorerPanel);
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
    public static void SelectResource(ResourceKey resource, bool showExplorerPanel)
    {
        var commandService = ServiceLocator.ServiceProvider.GetRequiredService<ICommandService>();
        commandService.Execute<ISelectResourceCommand>(command =>
        {
            command.Resource = resource;
            command.ShowExplorerPanel = showExplorerPanel;
        });
    }

    public static void SelectResource(ResourceKey resource)
    {
        SelectResource(resource, true);
    }
}
