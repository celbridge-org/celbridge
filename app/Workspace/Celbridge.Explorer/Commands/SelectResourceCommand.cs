using Celbridge.Commands;
using Celbridge.UserInterface;
using Celbridge.Workspace;

namespace Celbridge.Explorer.Commands;

public class SelectResourceCommand : CommandBase, ISelectResourceCommand
{
    private readonly ICommandService _commandService;
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey Resource { get; set; }

    public bool ShowExplorerPanel { get; set; } = true;

    public SelectResourceCommand(
        ICommandService commandService,
        IWorkspaceWrapper workspaceWrapper)
    {
        _commandService = commandService;
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var explorerService = _workspaceWrapper.WorkspaceService.ExplorerService;

        var selectResult = await explorerService.SelectResources([Resource]);
        if (selectResult.IsFailure)
        {
            return selectResult;
        }

        if (ShowExplorerPanel)
        {
            _commandService.Execute<ISetPanelVisibilityCommand>(command =>
            {
                command.Panels = PanelVisibilityFlags.Primary;
                command.IsVisible = true;
            });
        }

        return Result.Ok();
    }

    //
    // Static methods for scripting support.
    //
    public static void SelectResource(ResourceKey resource, bool showExplorerPanel)
    {
        var commandService = ServiceLocator.AcquireService<ICommandService>();

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
