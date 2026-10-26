using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Messaging;
using Celbridge.Resources;
using Celbridge.Search;
using Celbridge.Search.ViewModels;
using Celbridge.Tests.FileSystem;
using Celbridge.Tests.Helpers;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Search;

/// <summary>
/// Tests for the Search panel's searches. A search shows each file's results once found. The spinner stays across a
/// canceled search, and a refresh replaces the results at the end.
/// </summary>
[TestFixture]
public class SearchPanelViewModelTests
{
    private ISearchService _searchService = null!;
    private SearchPanelViewModel _viewModel = null!;

    [SetUp]
    public void Setup()
    {
        _searchService = Substitute.For<ISearchService>();

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Policy.Returns(TestResourcePolicy.CreateDefault());
        resourceService.Monitor.Returns(new FakeResourceMonitor());

        // A substitute's settable properties return the value last set, the way the settings store does.
        var workspaceSettings = Substitute.For<IBindableWorkspaceSettings>();
        workspaceSettings.ApplySearchExclude = true;

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);
        workspaceService.BindableWorkspaceSettings.Returns(workspaceSettings);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.IsWorkspaceLoaded.Returns(true);
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var stringLocalizer = Substitute.For<IStringLocalizer>();
        stringLocalizer[Arg.Any<string>()].Returns(call =>
        {
            var name = call.Arg<string>();
            return new LocalizedString(name, name);
        });
        stringLocalizer[Arg.Any<string>(), Arg.Any<object[]>()].Returns(call =>
        {
            var name = call.Arg<string>();
            var arguments = call.Arg<object[]>();
            return new LocalizedString(name, $"{name}({string.Join(", ", arguments)})");
        });

        // The tests start each search themselves, so the dispatcher drops the timers' work for the UI thread.
        var dispatcher = Substitute.For<IDispatcher>();

        _viewModel = new SearchPanelViewModel(
            _searchService,
            Substitute.For<ICommandService>(),
            Substitute.For<IMessengerService>(),
            workspaceWrapper,
            Substitute.For<IDialogService>(),
            stringLocalizer,
            dispatcher);
        _viewModel.SearchText = "hello";
    }

    [Test]
    public async Task Search_ShowsEachFileAsFound_BeforeTheSearchEnds()
    {
        var pendingSearch = UseSearches(1)[0];

        var search = _viewModel.ExecuteSearchAsync();
        pendingSearch.Find("a.txt");
        _viewModel.AddFoundResults();
        var resultsWhileSearching = ResultPaths();
        var statusWhileSearching = _viewModel.StatusText;

        pendingSearch.Find("b.txt");
        pendingSearch.End("a.txt", "b.txt");
        await search;

        resultsWhileSearching.Should().Equal("a.txt");
        statusWhileSearching.Should().BeEmpty("the spinner shows in place of the count");
        ResultPaths().Should().Equal("a.txt", "b.txt");
        _viewModel.IsSearching.Should().BeFalse();
        _viewModel.StatusText.Should().Be("SearchPanel_Status_NMatchesNFiles(2, 2)");
    }

    [Test]
    public async Task Spinner_StaysOn_WhenACanceledSearchEndsBeforeTheNewerSearch()
    {
        var pendingSearches = UseSearches(2);
        var canceledSearch = pendingSearches[0];
        var newerSearch = pendingSearches[1];

        var firstSearch = _viewModel.ExecuteSearchAsync();
        var secondSearch = _viewModel.ExecuteSearchAsync();

        canceledSearch.Find("stale.txt");
        canceledSearch.End("stale.txt");
        await firstSearch;
        _viewModel.AddFoundResults();
        var spinnerAfterCanceledSearch = _viewModel.IsSearching;
        var resultsAfterCanceledSearch = ResultPaths();

        newerSearch.Find("a.txt");
        newerSearch.End("a.txt");
        await secondSearch;

        spinnerAfterCanceledSearch.Should().BeTrue();
        resultsAfterCanceledSearch.Should().BeEmpty();
        _viewModel.IsSearching.Should().BeFalse();
        ResultPaths().Should().Equal("a.txt");
    }

    [Test]
    public async Task Refresh_KeepsTheResults_AndReplacesThemWhenItEnds()
    {
        var pendingSearches = UseSearches(2);
        var firstSearch = _viewModel.ExecuteSearchAsync();
        pendingSearches[0].Find("a.txt");
        pendingSearches[0].End("a.txt");
        await firstSearch;

        var refresh = _viewModel.ExecuteSearchAsync(isRefresh: true);
        var resultsWhileRefreshing = ResultPaths();
        var spinnerWhileRefreshing = _viewModel.IsSearching;

        pendingSearches[1].End("a.txt", "b.txt");
        await refresh;

        pendingSearches[1].FileResultFound.Should().BeNull("a refresh shows its results when it ends");
        resultsWhileRefreshing.Should().Equal("a.txt");
        spinnerWhileRefreshing.Should().BeTrue();
        ResultPaths().Should().Equal("a.txt", "b.txt");
        _viewModel.IsSearching.Should().BeFalse();
    }

    [Test]
    public async Task ChangingTheSearchText_ClearsTheResultsAtOnce()
    {
        var pendingSearch = UseSearches(1)[0];
        var search = _viewModel.ExecuteSearchAsync();
        pendingSearch.Find("a.txt");
        pendingSearch.End("a.txt");
        await search;
        var resultsBeforeTheChange = ResultPaths();

        _viewModel.SearchText = "goodbye";

        resultsBeforeTheChange.Should().Equal("a.txt");
        _viewModel.FileResults.Should().BeEmpty();
        _viewModel.HasResults.Should().BeFalse();
    }

    // Each call to the search service starts the next of these searches. The test ends each one.
    private PendingSearch[] UseSearches(int count)
    {
        var pendingSearches = Enumerable.Range(0, count)
            .Select(_ => new PendingSearch())
            .ToArray();

        var searchCount = 0;
        _searchService.SearchAsync(Arg.Any<SearchOptions>(), Arg.Any<Action<SearchFileResult>?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var pendingSearch = pendingSearches[searchCount];
                searchCount++;

                pendingSearch.FileResultFound = call.ArgAt<Action<SearchFileResult>?>(1);
                return pendingSearch.Task;
            });

        return pendingSearches;
    }

    private List<string> ResultPaths()
    {
        return _viewModel.FileResults
            .Select(fileResult => fileResult.Resource.Path)
            .ToList();
    }

    private static SearchFileResult FileResult(string path)
    {
        var match = new SearchMatchLine(1, "hello", 0, 5, 0);

        return new SearchFileResult(new ResourceKey(path), Path.GetFileName(path), path, [match]);
    }

    private static SearchResults Results(params string[] paths)
    {
        var fileResults = paths.Select(FileResult).ToList();

        return new SearchResults("hello", fileResults, fileResults.Count, fileResults.Count, false, false, fileResults.Count);
    }

    // A pending search, which ends when the test calls End. Find reports a file to the panel the way SearchService
    // does, once the search has read the file.
    private sealed class PendingSearch
    {
        private readonly TaskCompletionSource<SearchResults> _completion = new();

        public Action<SearchFileResult>? FileResultFound { get; set; }

        public Task<SearchResults> Task => _completion.Task;

        public void Find(string path)
        {
            FileResultFound?.Invoke(FileResult(path));
        }

        public void End(params string[] paths)
        {
            _completion.SetResult(Results(paths));
        }
    }
}
