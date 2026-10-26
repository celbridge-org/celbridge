using Celbridge.Search;
using Celbridge.Search.Commands;
using Celbridge.Workspace;

namespace Celbridge.Tests.Search;

/// <summary>
/// Tests for SearchFolderCommand, which shows the Search panel on a folder for Search in Folder.
/// </summary>
[TestFixture]
public class SearchFolderCommandTests
{
    private IUtilityPanel _utilityPanel = null!;
    private ISearchPanel _searchPanel = null!;
    private SearchFolderCommand _command = null!;

    [SetUp]
    public void Setup()
    {
        _searchPanel = Substitute.For<ISearchPanel>();

        _utilityPanel = Substitute.For<IUtilityPanel>();
        _utilityPanel.SearchPanel.Returns(_searchPanel);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.UtilityPanel.Returns(_utilityPanel);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        _command = new SearchFolderCommand(workspaceWrapper);
    }

    [Test]
    public async Task Execute_ShowsTheSearchPanelOnTheFolder_AndSearches()
    {
        _command.Folder = new ResourceKey("docs");

        var result = await _command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        Received.InOrder(() =>
        {
            _utilityPanel.ShowUtility(BuiltInUtilityIds.Search);
            _searchPanel.SetFolder(new ResourceKey("docs"));
            _searchPanel.ExecuteSearch();
            _searchPanel.FocusSearchInput();
        });
    }

    [Test]
    public async Task Execute_LeavesTheSearchTextAndOptionsUnchanged()
    {
        _command.Folder = new ResourceKey("docs");

        await _command.ExecuteAsync();

        _searchPanel.DidNotReceive().SetSearchText(Arg.Any<string>());
        _searchPanel.DidNotReceive().SetMatchCase(Arg.Any<bool>());
        _searchPanel.DidNotReceive().SetWholeWord(Arg.Any<bool>());
        _searchPanel.DidNotReceive().SetReplaceMode(Arg.Any<bool>());
        _searchPanel.DidNotReceive().SetReplaceText(Arg.Any<string>());
    }
}
