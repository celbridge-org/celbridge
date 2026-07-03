using Celbridge.UserInterface.Services;
using Celbridge.Workspace;

namespace Celbridge.Explorer.Services;

/// <summary>
/// Prepares an Explorer landmark for spotlighting by switching the activity panel to the Explorer
/// tab, since the Explorer content is collapsed while another activity (such as Search) is active.
/// For the toolbar buttons it also fades in the ephemeral toolbar, which it hides again on clear.
/// </summary>
public sealed class ExplorerSpotlightLandmark : ISpotlightLandmark
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly bool _revealToolbar;

    public ExplorerSpotlightLandmark(IWorkspaceWrapper workspaceWrapper, bool revealToolbar)
    {
        _workspaceWrapper = workspaceWrapper;
        _revealToolbar = revealToolbar;
    }

    public async Task<Result> PreSpotlightAsync()
    {
        await Task.CompletedTask;

        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail("Cannot reveal the Explorer landmark: no workspace is loaded.");
        }

        var activityPanel = _workspaceWrapper.WorkspaceService.ActivityPanel;

        // Switch to the Explorer tab so the Explorer content is on screen; it is collapsed while
        // another activity (such as Search) is the active tab.
        activityPanel.ShowTab(ActivityPanelTab.Explorer);

        if (_revealToolbar)
        {
            activityPanel.ExplorerPanel.SetToolbarRevealed(true);
        }

        return Result.Ok();
    }

    public void PostSpotlight()
    {
        if (!_revealToolbar ||
            !_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return;
        }

        var explorerPanel = _workspaceWrapper.WorkspaceService.ActivityPanel.ExplorerPanel;
        explorerPanel.SetToolbarRevealed(false);
    }
}
