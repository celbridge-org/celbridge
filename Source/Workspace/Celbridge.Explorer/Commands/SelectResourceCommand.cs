using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Explorer.Commands;

public class SelectResourceCommand : CommandBase, ISelectResourceCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ResourceKey Resource { get; set; }

    public bool ShowExplorerPanel { get; set; } = true;

    public SelectResourceCommand(IWorkspaceWrapper workspaceWrapper)
    {
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
            var utilityPanel = _workspaceWrapper.WorkspaceService.UtilityPanel;
            utilityPanel.ShowUtility(BuiltInUtilityIds.Explorer);
        }

        return Result.Ok();
    }
}
