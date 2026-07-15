using Celbridge.UserInterface.Services;
using Celbridge.Workspace;

namespace Celbridge.Search.Services;

/// <summary>
/// Prepares a Search landmark for spotlighting by switching the Utility Panel to the Search tab,
/// since the Search content is collapsed while another activity (such as Explorer) is active. For
/// the replace controls it also enables replace mode, which it restores to its prior state on clear.
/// </summary>
public sealed class SearchSpotlightLandmark : ISpotlightLandmark
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly bool _revealReplace;

    // Whether replace mode was already on when this reveal enabled it, so PostSpotlight only turns
    // it back off if the reveal was what turned it on.
    private bool _replaceModeWasEnabled;

    public SearchSpotlightLandmark(IWorkspaceWrapper workspaceWrapper, bool revealReplace)
    {
        _workspaceWrapper = workspaceWrapper;
        _revealReplace = revealReplace;
    }

    public async Task<Result> PreSpotlightAsync()
    {
        await Task.CompletedTask;

        if (!_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return Result.Fail("Cannot reveal the Search landmark: no workspace is loaded.");
        }

        var searchPanel = _workspaceWrapper.WorkspaceService.UtilityPanel.SearchPanel;

        // Switch to the Search tab so the Search content is on screen; it is collapsed while another
        // activity (such as Explorer) is the active tab.
        _workspaceWrapper.WorkspaceService.UtilityPanel.ShowUtility(BuiltInUtilityIds.Search);

        if (_revealReplace)
        {
            _replaceModeWasEnabled = searchPanel.IsReplaceModeEnabled;
            searchPanel.SetReplaceMode(true);
        }

        return Result.Ok();
    }

    public void PostSpotlight()
    {
        if (!_revealReplace ||
            _replaceModeWasEnabled ||
            !_workspaceWrapper.IsWorkspacePageLoaded)
        {
            return;
        }

        _workspaceWrapper.WorkspaceService.UtilityPanel.SearchPanel.SetReplaceMode(false);
    }
}
