using Celbridge.UserInterface.Services;
using Celbridge.Workspace;

namespace Celbridge.Explorer.Services;

/// <summary>
/// Prepares an Explorer landmark for spotlighting by switching the Utility Panel to the Explorer
/// tab, since the Explorer content is collapsed while another activity (such as Search) is active.
/// </summary>
public sealed class ExplorerSpotlightLandmark : ISpotlightLandmark
{
    private readonly IWorkspaceWrapper _workspaceWrapper;

    public ExplorerSpotlightLandmark(IWorkspaceWrapper workspaceWrapper)
    {
        _workspaceWrapper = workspaceWrapper;
    }

    public async Task<Result> PreSpotlightAsync()
    {
        await Task.CompletedTask;

        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail("Cannot reveal the Explorer landmark: no workspace is loaded.");
        }

        // Switch to the Explorer tab so the Explorer content is on screen; it is collapsed while
        // another activity (such as Search) is the active tab. The toolbar is persistent, so no reveal
        // is needed for its buttons.
        _workspaceWrapper.WorkspaceService.UtilityPanel.ShowUtility(BuiltInUtilityIds.Explorer);

        return Result.Ok();
    }

    public void PostSpotlight()
    {
        // Nothing to undo: switching to the Explorer tab is left in place, and the toolbar is persistent.
    }
}
