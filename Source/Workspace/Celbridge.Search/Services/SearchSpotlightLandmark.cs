using Celbridge.UserInterface.Services;
using Celbridge.Workspace;

namespace Celbridge.Search.Services;

/// <summary>
/// Picks the controls to show with the Search tab when a Search landmark is spotlighted.
/// </summary>
public enum SearchLandmarkReveal
{
    /// <summary>
    /// The landmark shows whenever the Search tab is active.
    /// </summary>
    SearchTab,

    /// <summary>
    /// The landmark shows only with replace mode on.
    /// </summary>
    ReplaceControls,

    /// <summary>
    /// The landmark shows only with the search filters shown.
    /// </summary>
    FilterControls,
}

/// <summary>
/// Prepares a Search landmark for spotlighting by switching the Utility Panel to the Search tab,
/// since the Search content is collapsed while another activity, like the Explorer, is active. A
/// replace landmark also turns on replace mode, and a filters landmark also shows the search filters.
/// Clearing the spotlight hides those controls again, unless they were already shown.
/// </summary>
public sealed class SearchSpotlightLandmark : ISpotlightLandmark
{
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly SearchLandmarkReveal _reveal;

    // True when the revealed controls were shown before the spotlight. PostSpotlight then leaves them shown.
    private bool _controlsWereShown;

    public SearchSpotlightLandmark(IWorkspaceWrapper workspaceWrapper, SearchLandmarkReveal reveal)
    {
        _workspaceWrapper = workspaceWrapper;
        _reveal = reveal;
    }

    public async Task<Result> PreSpotlightAsync()
    {
        await Task.CompletedTask;

        if (!_workspaceWrapper.IsWorkspaceLoaded)
        {
            return Result.Fail("Cannot reveal the Search landmark: no workspace is loaded.");
        }

        var searchPanel = _workspaceWrapper.WorkspaceService.UtilityPanel.SearchPanel;

        // Switch to the Search tab so the Search content is on screen; it is collapsed while another
        // activity (such as Explorer) is the active tab.
        _workspaceWrapper.WorkspaceService.UtilityPanel.ShowUtility(BuiltInUtilityIds.Search);

        if (_reveal == SearchLandmarkReveal.ReplaceControls)
        {
            _controlsWereShown = searchPanel.IsReplaceModeEnabled;
            searchPanel.SetReplaceMode(true);
        }
        else if (_reveal == SearchLandmarkReveal.FilterControls)
        {
            _controlsWereShown = searchPanel.AreFiltersVisible;
            searchPanel.SetFiltersVisible(true);
        }

        return Result.Ok();
    }

    public void PostSpotlight()
    {
        if (_reveal == SearchLandmarkReveal.SearchTab
            || _controlsWereShown
            || !_workspaceWrapper.IsWorkspaceLoaded)
        {
            return;
        }

        var searchPanel = _workspaceWrapper.WorkspaceService.UtilityPanel.SearchPanel;
        if (_reveal == SearchLandmarkReveal.ReplaceControls)
        {
            searchPanel.SetReplaceMode(false);
        }
        else
        {
            searchPanel.SetFiltersVisible(false);
        }
    }
}
