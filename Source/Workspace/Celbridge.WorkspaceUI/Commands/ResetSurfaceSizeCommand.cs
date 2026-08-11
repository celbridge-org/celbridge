using Celbridge.Commands;

namespace Celbridge.WorkspaceUI.Commands;

public class ResetSurfaceSizeCommand : CommandBase, IResetSurfaceSizeCommand
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public WorkspaceSurface Surface { get; set; }

    public ResetSurfaceSizeCommand(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public override async Task<Result> ExecuteAsync()
    {
        var workspaceSettings = _workspaceWrapper.WorkspaceService.BindableWorkspaceSettings;

        switch (Surface)
        {
            case WorkspaceSurface.UtilityPanel:
                workspaceSettings.UtilityPanelWidth = WorkspaceConstants.UtilityPanelWidth;
                break;

            case WorkspaceSurface.SideArea:
                workspaceSettings.SideAreaWidth = WorkspaceConstants.SideAreaWidth;
                break;

            case WorkspaceSurface.BottomArea:
                workspaceSettings.BottomAreaHeight = WorkspaceConstants.BottomAreaHeight;
                break;

            default:
                return Result.Fail($"Unknown surface: {Surface}");
        }

        await Task.CompletedTask;

        return Result.Ok();
    }
}
