using Celbridge.Commands;

namespace Celbridge.WorkspaceUI.Commands;

public class ResetPanelCommand : CommandBase, IResetPanelCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public LayoutRegion Region { get; set; }

    public ResetPanelCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var workspaceSettings = _workspaceWrapper.WorkspaceService.BindableWorkspaceSettings;

        switch (Region)
        {
            case LayoutRegion.UtilityPanel:
                workspaceSettings.UtilityPanelWidth = WorkspaceConstants.UtilityPanelWidth;
                break;

            case LayoutRegion.SideArea:
                workspaceSettings.SideAreaWidth = WorkspaceConstants.SideAreaWidth;
                break;

            case LayoutRegion.BottomArea:
                workspaceSettings.BottomAreaHeight = WorkspaceConstants.BottomAreaHeight;
                break;

            default:
                return Result.Fail($"Unknown region: {Region}");
        }

        await Task.CompletedTask;

        return Result.Ok();
    }
}
