using Celbridge.Search;
using Celbridge.Search.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.Search;

/// <summary>
/// Tests for SearchSpotlightLandmark, which shows the Search controls for a landmark, and hides them again
/// afterwards.
/// </summary>
[TestFixture]
public class SearchSpotlightLandmarkTests
{
    private IUtilityPanel _utilityPanel = null!;
    private ISearchPanel _searchPanel = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;

    [SetUp]
    public void Setup()
    {
        _searchPanel = Substitute.For<ISearchPanel>();

        _utilityPanel = Substitute.For<IUtilityPanel>();
        _utilityPanel.SearchPanel.Returns(_searchPanel);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.UtilityPanel.Returns(_utilityPanel);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.IsWorkspaceLoaded.Returns(true);
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);
    }

    [Test]
    public async Task ScopeLandmark_ShowsTheScope_AndHidesItAgainOnClear()
    {
        _searchPanel.IsScopeVisible.Returns(false);
        var landmark = new SearchSpotlightLandmark(_workspaceWrapper, SearchLandmarkReveal.ScopeControls);

        var revealResult = await landmark.PreSpotlightAsync();
        landmark.PostSpotlight();

        revealResult.IsSuccess.Should().BeTrue();
        _utilityPanel.Received(1).ShowUtility(BuiltInUtilityIds.Search);
        Received.InOrder(() =>
        {
            _searchPanel.SetScopeVisible(true);
            _searchPanel.SetScopeVisible(false);
        });
        _searchPanel.DidNotReceive().SetReplaceMode(Arg.Any<bool>());
    }

    [Test]
    public async Task ScopeLandmark_LeavesAShownScopeShownOnClear()
    {
        _searchPanel.IsScopeVisible.Returns(true);
        var landmark = new SearchSpotlightLandmark(_workspaceWrapper, SearchLandmarkReveal.ScopeControls);

        await landmark.PreSpotlightAsync();
        landmark.PostSpotlight();

        _searchPanel.DidNotReceive().SetScopeVisible(false);
    }

    [Test]
    public async Task ReplaceLandmark_TurnsOnReplaceMode_AndLeavesTheScopeAlone()
    {
        _searchPanel.IsReplaceModeEnabled.Returns(false);
        var landmark = new SearchSpotlightLandmark(_workspaceWrapper, SearchLandmarkReveal.ReplaceControls);

        await landmark.PreSpotlightAsync();
        landmark.PostSpotlight();

        Received.InOrder(() =>
        {
            _searchPanel.SetReplaceMode(true);
            _searchPanel.SetReplaceMode(false);
        });
        _searchPanel.DidNotReceive().SetScopeVisible(Arg.Any<bool>());
    }

    [Test]
    public async Task SearchTabLandmark_OnlyShowsTheSearchTab()
    {
        var landmark = new SearchSpotlightLandmark(_workspaceWrapper, SearchLandmarkReveal.SearchTab);

        await landmark.PreSpotlightAsync();
        landmark.PostSpotlight();

        _utilityPanel.Received(1).ShowUtility(BuiltInUtilityIds.Search);
        _searchPanel.DidNotReceive().SetScopeVisible(Arg.Any<bool>());
        _searchPanel.DidNotReceive().SetReplaceMode(Arg.Any<bool>());
    }
}
