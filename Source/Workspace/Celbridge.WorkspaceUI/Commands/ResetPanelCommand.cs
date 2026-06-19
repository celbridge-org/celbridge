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
            case LayoutRegion.Primary:
                workspaceSettings.PrimaryPanelWidth = WorkspaceConstants.PrimaryPanelWidth;
                break;

            case LayoutRegion.Secondary:
                workspaceSettings.SecondaryPanelWidth = WorkspaceConstants.SecondaryPanelWidth;
                break;

            case LayoutRegion.Console:
                workspaceSettings.ConsolePanelHeight = WorkspaceConstants.ConsolePanelHeight;
                break;

            default:
                return Result.Fail($"Unknown region: {Region}");
        }

        await Task.CompletedTask;

        return Result.Ok();
    }
}
