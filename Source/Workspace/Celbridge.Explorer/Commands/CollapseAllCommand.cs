using Celbridge.Commands;
using Celbridge.Workspace;

namespace Celbridge.Explorer.Commands;

public class CollapseAllCommand : CommandBase, ICollapseAllCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    public override CommandFlags CommandFlags => CommandFlags.SaveWorkspaceState;

    public CollapseAllCommand(
        IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var explorerPanel = _workspaceWrapper.WorkspaceService.UtilityPanel.ExplorerPanel;
        await explorerPanel.CollapseAllFoldersAsync();

        return Result.Ok();
    }
}
