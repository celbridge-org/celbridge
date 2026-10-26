using Celbridge.Commands;
using Celbridge.Dialog;
using Celbridge.Documents;
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
    private ICommandService _commandService = null!;
    private MessageHandler<object, DocumentSaveCompletedMessage> _saveCompletedHandler = null!;
    private SearchPanelViewModel _viewModel = null!;

    [SetUp]
    public void Setup()
    {
        _searchService = Substitute.For<ISearchService>();
        _commandService = Substitute.For<ICommandService>();

        var messengerService = Substitute.For<IMessengerService>();
        messengerService
            .When(service => service.Register(
                Arg.Any<object>(),
                Arg.Any<MessageHandler<object, DocumentSaveCompletedMessage>>()))
            .Do(call => _saveCompletedHandler = call.Arg<MessageHandler<object, DocumentSaveCompletedMessage>>());

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Policy.Returns(TestResourcePolicy.CreateDefault());

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
            _commandService,
            messengerService,
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
    public async Task Refresh_ExpandsANewFile_OnlyWithinTheLimitOfExpandedMatches()
    {
        var pendingSearches = UseSearches(2);
        var firstSearch = _viewModel.ExecuteSearchAsync();
        pendingSearches[0].Find("a.txt");
        pendingSearches[0].End("a.txt");
        await firstSearch;

        var refresh = _viewModel.ExecuteSearchAsync(isRefresh: true);
        pendingSearches[1].EndWith(
            FileResult("a.txt"),
            FileResult("b.txt", matchCount: 60),
            FileResult("c.txt", matchCount: 60));
        await refresh;

        var expandedPaths = _viewModel.FileResults
            .Where(fileResult => fileResult.IsExpanded)
            .Select(fileResult => fileResult.Resource.Path);
        expandedPaths.Should().Equal("a.txt", "b.txt");
    }

    [Test]
    public async Task FailedSearch_ShowsTheErrorInTheStatusBar()
    {
        _searchService.SearchAsync(Arg.Any<SearchOptions>(), Arg.Any<Action<SearchFileResult>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<SearchResults>(new InvalidOperationException("The search failed.")));

        await _viewModel.ExecuteSearchAsync();

        _viewModel.StatusText.Should().Be("SearchPanel_Error");
        _viewModel.ShowStatusBar.Should().BeTrue();
    }

    [Test]
    public async Task Refresh_DuringASearchThatShowsEachFile_ShowsEachFileToo()
    {
        var pendingSearches = UseSearches(2);
        var firstSearch = _viewModel.ExecuteSearchAsync();
        pendingSearches[0].Find("a.txt");
        _viewModel.AddFoundResults();

        var refresh = _viewModel.ExecuteSearchAsync(isRefresh: true);
        pendingSearches[0].End("a.txt");
        await firstSearch;
        pendingSearches[1].Find("b.txt");
        _viewModel.AddFoundResults();
        var resultsWhileRefreshing = ResultPaths();

        pendingSearches[1].End("b.txt");
        await refresh;

        pendingSearches[1].FileResultFound.Should().NotBeNull("the list of the canceled search was unfinished");
        resultsWhileRefreshing.Should().Equal("b.txt");
        ResultPaths().Should().Equal("b.txt");
    }

    [Test]
    public async Task ASearchStartedAtOnce_DropsTheSearchWaitingOnTheDebounceTimer()
    {
        var pendingSearch = UseSearches(1)[0];
        var pendingBeforeTheSearch = _viewModel.IsSearchPending;

        var search = _viewModel.ExecuteSearchAsync();
        var pendingDuringTheSearch = _viewModel.IsSearchPending;
        pendingSearch.End();
        await search;

        pendingBeforeTheSearch.Should().BeTrue("the setup typed the search text");
        pendingDuringTheSearch.Should().BeFalse();
    }

    [Test]
    public async Task SavingADocument_SearchesThatFileAgain_AndUpdatesOnlyItsEntry()
    {
        var pendingSearches = UseSearches(3);
        var search = _viewModel.ExecuteSearchAsync();
        pendingSearches[0].Find("a.txt");
        pendingSearches[0].Find("c.txt");
        pendingSearches[0].End("a.txt", "c.txt");
        await search;

        SaveDocument("b.txt");
        pendingSearches[1].End("b.txt");
        var resultsAfterSavingB = ResultPaths();
        SaveDocument("a.txt");
        pendingSearches[2].End();

        pendingSearches[1].Options!.Files.Should().Equal(new ResourceKey("b.txt"));
        resultsAfterSavingB.Should().Equal("a.txt", "b.txt", "c.txt");
        ResultPaths().Should().Equal("b.txt", "c.txt");
    }

    [Test]
    public async Task ReplaceMatch_WithTheFileUnchanged_ReplacesTheMatch()
    {
        var pendingSearches = UseSearches(2);
        await ShowResultsAsync(pendingSearches[0], "a.txt");
        var match = _viewModel.FileResults[0].Matches[0];

        var replace = _viewModel.ReplaceMatchAsync(match);
        pendingSearches[1].End("a.txt");
        await replace;

        RangeEditCount().Should().Be(1);
        _viewModel.FileResults.Should().BeEmpty();
    }

    [Test]
    public async Task ReplaceMatch_WithTheFileChangedSinceTheSearch_ShowsTheNewMatchesAndReplacesNothing()
    {
        var pendingSearches = UseSearches(2);
        await ShowResultsAsync(pendingSearches[0], "a.txt");
        var match = _viewModel.FileResults[0].Matches[0];

        var replace = _viewModel.ReplaceMatchAsync(match);
        pendingSearches[1].EndWith(FileResult("a.txt", matchCount: 2));
        await replace;

        RangeEditCount().Should().Be(0);
        _viewModel.FileResults.Single().MatchCount.Should().Be(2);
    }

    [Test]
    public async Task ReplaceInFile_WithTheFileChangedSinceTheSearch_ShowsTheNewMatchesAndReplacesNothing()
    {
        var pendingSearches = UseSearches(2);
        await ShowResultsAsync(pendingSearches[0], "a.txt");

        var replace = _viewModel.ReplaceInFileAsync(_viewModel.FileResults[0]);
        pendingSearches[1].EndWith(FileResult("a.txt", matchCount: 2));
        await replace;

        RangeEditCount().Should().Be(0);
        _viewModel.FileResults.Single().MatchCount.Should().Be(2);
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

                pendingSearch.Options = call.ArgAt<SearchOptions>(0);
                pendingSearch.FileResultFound = call.ArgAt<Action<SearchFileResult>?>(1);
                return pendingSearch.Task;
            });

        return pendingSearches;
    }

    // Shows the results of a search that finds the files.
    private async Task ShowResultsAsync(PendingSearch pendingSearch, params string[] paths)
    {
        var search = _viewModel.ExecuteSearchAsync();
        foreach (var path in paths)
        {
            pendingSearch.Find(path);
        }
        pendingSearch.End(paths);
        await search;
        _viewModel.ReplaceText = "goodbye";
    }

    private void SaveDocument(string path)
    {
        _saveCompletedHandler(this, new DocumentSaveCompletedMessage(new ResourceKey(path)));
    }

    // Counts the range edits the panel sent to the command service.
    private int RangeEditCount()
    {
        return _commandService.ReceivedCalls()
            .Count(call => call.GetMethodInfo().Name == nameof(ICommandService.ExecuteAsync)
                && call.GetMethodInfo().GetGenericArguments().FirstOrDefault() == typeof(IApplyRangeEditsCommand));
    }

    private List<string> ResultPaths()
    {
        return _viewModel.FileResults
            .Select(fileResult => fileResult.Resource.Path)
            .ToList();
    }

    private static SearchFileResult FileResult(string path, int matchCount = 1)
    {
        var matches = Enumerable.Range(1, matchCount)
            .Select(lineNumber => new SearchMatchLine(lineNumber, "hello", 0, 5, 0))
            .ToList();

        return new SearchFileResult(new ResourceKey(path), Path.GetFileName(path), path, matches);
    }

    private static SearchResults Results(IReadOnlyList<SearchFileResult> fileResults)
    {
        var matchCount = fileResults.Sum(fileResult => fileResult.Matches.Count);

        return new SearchResults("hello", fileResults.ToList(), matchCount, fileResults.Count, false, false, fileResults.Count);
    }

    // A pending search, which ends when the test calls End. Find reports a file to the panel the way SearchService
    // does, once the search has read the file.
    private sealed class PendingSearch
    {
        private readonly TaskCompletionSource<SearchResults> _completion = new();

        public SearchOptions? Options { get; set; }

        public Action<SearchFileResult>? FileResultFound { get; set; }

        public Task<SearchResults> Task => _completion.Task;

        public void Find(string path)
        {
            FileResultFound?.Invoke(FileResult(path));
        }

        public void End(params string[] paths)
        {
            var fileResults = paths
                .Select(path => FileResult(path))
                .ToArray();
            EndWith(fileResults);
        }

        public void EndWith(params SearchFileResult[] fileResults)
        {
            _completion.SetResult(Results(fileResults));
        }
    }
}
