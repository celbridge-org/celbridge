using Celbridge.Commands;

namespace Celbridge.WorkspaceUI.Commands;

public class ResetAreaSizeCommand : CommandBase, IResetAreaSizeCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public WorkspaceArea Area { get; set; }

    public ResetAreaSizeCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var workspaceSettings = _workspaceWrapper.WorkspaceService.BindableWorkspaceSettings;

        switch (Area)
        {
            case WorkspaceArea.Utility:
                workspaceSettings.UtilityPanelWidth = WorkspaceConstants.UtilityPanelWidth;
                break;

            case WorkspaceArea.Side:
                workspaceSettings.SideAreaWidth = WorkspaceConstants.SideAreaWidth;
                break;

            case WorkspaceArea.Bottom:
                workspaceSettings.BottomAreaHeight = WorkspaceConstants.BottomAreaHeight;
                break;

            default:
                return Result.Fail($"Area has no size of its own: {Area}");
        }

        await Task.CompletedTask;

        return Result.Ok();
    }
}
