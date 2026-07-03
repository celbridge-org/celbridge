using Celbridge.UserInterface.Services;
using Celbridge.Workspace;

namespace Celbridge.Search.Services;

/// <summary>
/// Prepares a Search landmark for spotlighting by switching the activity panel to the Search tab,
/// since the Search content is collapsed while another activity (such as Explorer) is active.
/// </summary>
public sealed class SearchSpotlightLandmark : ISpotlightLandmark
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public SearchSpotlightLandmark(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public async Task<Result> PreSpotlightAsync()
    {
        await Task.CompletedTask;

        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail("Cannot reveal the Search landmark: no workspace is loaded.");
        }

        _workspaceWrapper.WorkspaceService.ActivityPanel.ShowTab(ActivityPanelTab.Search);

        return Result.Ok();
    }

    public void PostSpotlight()
    {
    }
}
