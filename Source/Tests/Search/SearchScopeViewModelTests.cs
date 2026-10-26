using Celbridge.Dialog;
using Celbridge.Messaging.Services;
using Celbridge.Patterns;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Search;
using Celbridge.Search.ViewModels;
using Celbridge.Tests.FileSystem;
using Celbridge.Tests.Helpers;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Search;

/// <summary>
/// Tests for the Search panel's scope: the folder, the file filters and the search list toggle. Covers the workspace
/// settings and the search options. Covers the line that names the scope, and the subscription to the searched files.
/// </summary>
[TestFixture]
public class SearchScopeViewModelTests
{
    private string _projectFolderPath = null!;
    private IResourceService _resourceService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private IBindableWorkspaceSettings _workspaceSettings = null!;
    private IDialogService _dialogService = null!;
    private IStringLocalizer _stringLocalizer = null!;
    private FakeResourceMonitor _resourceMonitor = null!;

    [SetUp]
    public void Setup()
    {
        _projectFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(SearchScopeViewModelTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectFolderPath);

        _resourceMonitor = new FakeResourceMonitor();

        _resourceService = Substitute.For<IResourceService>();
        _resourceService.RootHandlers.Returns(TestRootHandlers.Create(_projectFolderPath));
        _resourceService.Policy.Returns(TestResourcePolicy.CreateDefault());
        _resourceService.Monitor.Returns(_resourceMonitor);

        // A substitute's settable properties return the value last set, the way the settings store does.
        _workspaceSettings = Substitute.For<IBindableWorkspaceSettings>();
        _workspaceSettings.ApplySearchExclude = true;

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(_resourceService);
        workspaceService.BindableWorkspaceSettings.Returns(_workspaceSettings);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.IsWorkspaceLoaded.Returns(true);
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var resourceFileSystem = new LocalResourceFileSystem(
            Substitute.For<ILogger<LocalResourceFileSystem>>(),
            new MessengerService(),
            _workspaceWrapper,
            TestFileSystem.CreateLocal());
        _resourceService.FileSystem.Returns(resourceFileSystem);

        _dialogService = Substitute.For<IDialogService>();

        // A composed string carries its arguments, so a test can tell which values reached the template.
        _stringLocalizer = Substitute.For<IStringLocalizer>();
        _stringLocalizer[Arg.Any<string>()].Returns(call =>
        {
            var name = call.Arg<string>();
            return new LocalizedString(name, name);
        });
        _stringLocalizer[Arg.Any<string>(), Arg.Any<object[]>()].Returns(call =>
        {
            var name = call.Arg<string>();
            var arguments = call.Arg<object[]>();
            return new LocalizedString(name, $"{name}({string.Join(", ", arguments)})");
        });
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_projectFolderPath))
        {
            Directory.Delete(_projectFolderPath, true);
        }
    }

    [Test]
    public void Constructor_ReadsTheScopeFromTheWorkspaceSettings()
    {
        _workspaceSettings.SearchScopeVisible = true;
        _workspaceSettings.SearchFolder = "project:docs";
        _workspaceSettings.SearchFilesToInclude = "*.md";
        _workspaceSettings.SearchFilesToExclude = "drafts/";
        _workspaceSettings.ApplySearchExclude = false;

        var scope = CreateScope();

        scope.IsVisible.Should().BeTrue();
        scope.Folder.Should().Be(new ResourceKey("docs"));
        scope.FolderText.Should().Be("docs");
        scope.HasFolder.Should().BeTrue();
        scope.FilesToInclude.Should().Be("*.md");
        scope.FilesToExclude.Should().Be("drafts/");
        scope.ApplySearchExclude.Should().BeFalse();
    }

    [Test]
    public void Changes_AreKeptInTheWorkspaceSettings()
    {
        var scope = CreateScope();

        scope.IsVisible = true;
        scope.SetFolder(new ResourceKey("docs"));
        scope.FilesToInclude = "*.md";
        scope.FilesToExclude = "drafts/";
        scope.ApplySearchExclude = false;

        _workspaceSettings.SearchScopeVisible.Should().BeTrue();
        _workspaceSettings.SearchFolder.Should().Be("project:docs");
        _workspaceSettings.SearchFilesToInclude.Should().Be("*.md");
        _workspaceSettings.SearchFilesToExclude.Should().Be("drafts/");
        _workspaceSettings.ApplySearchExclude.Should().BeFalse();
    }

    [Test]
    public void Changed_IsTypedForAFilter_AndNotForTheFolderOrTheSearchList()
    {
        var scope = CreateScope();
        var changes = new List<SearchScopeChange>();
        scope.Changed += (_, change) => changes.Add(change);

        scope.FilesToInclude = "*.md";
        scope.SetFolder(new ResourceKey("docs"));
        scope.ApplySearchExclude = false;
        scope.IsVisible = true;

        changes.Select(change => change.IsTyped).Should().Equal(true, false, false);
    }

    [Test]
    public async Task CreateSearchOptions_CarriesTheFolderFiltersAndSearchListToggle()
    {
        Directory.CreateDirectory(Path.Combine(_projectFolderPath, "docs"));
        var scope = CreateScope();
        scope.SetFolder(new ResourceKey("docs"));
        scope.FilesToInclude = "*.md";
        scope.FilesToExclude = "drafts/";
        scope.ApplySearchExclude = false;

        var options = await scope.CreateSearchOptionsAsync(Matcher("hello"), maxResults: 1000);

        options.Should().NotBeNull();
        options!.Matcher.Pattern.Should().Be("hello");
        options.Scope.Should().Be("project:docs");
        options.MaxResults.Should().Be(1000);
        options.ApplySearchExclude.Should().BeFalse();
        options.Filter.IsMatch("docs/guide.md", isFolder: false).Should().BeTrue();
        options.Filter.IsMatch("docs/guide.txt", isFolder: false).Should().BeFalse();
        options.Filter.IsMatch("docs/drafts/guide.md", isFolder: false).Should().BeFalse();
    }

    [Test]
    public async Task CreateSearchOptions_ForReplaceAll_HasNoCap()
    {
        var scope = CreateScope();

        var options = await scope.CreateSearchOptionsAsync(Matcher("hello"), maxResults: null);

        options.Should().NotBeNull();
        options!.MaxResults.Should().BeNull();
        options.Scope.Should().BeEmpty();
        options.ApplySearchExclude.Should().BeTrue();
    }

    [Test]
    public async Task CreateSearchOptions_ClearsAMissingFolderFirst()
    {
        _workspaceSettings.SearchFolder = "project:gone";
        var scope = CreateScope();
        var changes = new List<SearchScopeChange>();
        scope.Changed += (_, change) => changes.Add(change);

        var options = await scope.CreateSearchOptionsAsync(Matcher("hello"), maxResults: 1000);

        options!.Scope.Should().BeEmpty();
        scope.Folder.IsEmpty.Should().BeTrue();
        scope.FolderText.Should().BeEmpty();
        scope.HasFolder.Should().BeFalse();
        _workspaceSettings.SearchFolder.Should().BeEmpty();
        changes.Should().BeEmpty("the search that cleared the folder is already running");
    }

    [Test]
    public async Task RefusedFilter_ShowsTheInvalidLine_AndStopsTheSearch()
    {
        var scope = CreateScope();

        scope.FilesToInclude = "*.{md";
        var refusedOptions = await scope.CreateSearchOptionsAsync(Matcher("hello"), maxResults: 1000);
        var refusedLine = scope.HasInvalidFilter;

        scope.FilesToInclude = "*.{md,txt}";
        var acceptedOptions = await scope.CreateSearchOptionsAsync(Matcher("hello"), maxResults: 1000);

        refusedLine.Should().BeTrue();
        refusedOptions.Should().BeNull();
        scope.HasInvalidFilter.Should().BeFalse();
        acceptedOptions.Should().NotBeNull();
        scope.InvalidFilterText.Should().Be("SearchPanel_InvalidFilter");
    }

    [Test]
    public void Summary_NamesTheFolder()
    {
        var scope = CreateScope();

        scope.SetFolder(new ResourceKey("docs/api"));

        scope.ShowSummary.Should().BeTrue();
        scope.SummaryText.Should().Be("SearchPanel_ScopeSummary_Folder(docs/api)");
    }

    [TestCase("*.md", "", true)]
    [TestCase("", "drafts/", true)]
    [TestCase("", "", false)]
    public void Summary_SaysWhenTheFiltersDifferFromTheirDefaults(string filesToInclude, string filesToExclude, bool applySearchExclude)
    {
        var scope = CreateScope();

        scope.FilesToInclude = filesToInclude;
        scope.FilesToExclude = filesToExclude;
        scope.ApplySearchExclude = applySearchExclude;

        scope.ShowSummary.Should().BeTrue();
        scope.SummaryText.Should().Be("SearchPanel_ScopeSummary_Filters");
    }

    [Test]
    public void Summary_NamesTheFolder_AndSaysTheFiltersDiffer()
    {
        var scope = CreateScope();

        scope.SetFolder(new ResourceKey("docs"));
        scope.FilesToInclude = "*.md";

        scope.SummaryText.Should().Be("SearchPanel_ScopeSummary_FolderAndFilters(docs)");
    }

    [Test]
    public void Summary_IsEmpty_ForWhitespaceFilters()
    {
        var scope = CreateScope();

        scope.FilesToInclude = "  ";

        scope.ShowSummary.Should().BeFalse();
        scope.SummaryText.Should().BeEmpty();
    }

    [Test]
    public void Summary_IsHidden_WhileTheScopeIsShownOrAtItsDefault()
    {
        var scope = CreateScope();
        var shownAtDefault = scope.ShowSummary;

        scope.FilesToInclude = "*.md";
        var shownWhileHidden = scope.ShowSummary;

        scope.IsVisible = true;
        var shownWhileVisible = scope.ShowSummary;

        shownAtDefault.Should().BeFalse();
        shownWhileHidden.Should().BeTrue();
        shownWhileVisible.Should().BeFalse();
    }

    [Test]
    public void Show_ShowsTheScope()
    {
        var scope = CreateScope();
        scope.FilesToInclude = "*.md";

        scope.ShowCommand.Execute(null);

        scope.IsVisible.Should().BeTrue();
        scope.ShowSummary.Should().BeFalse();
    }

    [Test]
    public void Subscription_UsesTheSearchList_WhileTheListApplies()
    {
        var policy = UseSearchExclude("dist");

        CreateScope();

        _resourceMonitor.MatcherSubscriptions.Should().ContainSingle()
            .Which.Matcher.Should().BeSameAs(policy.SearchScope);
    }

    [Test]
    public void Subscription_FollowsEveryPath_WithTheSearchListOff()
    {
        var policy = UseSearchExclude("dist");
        var scope = CreateScope();

        scope.ApplySearchExclude = false;
        var matcherWhileOff = _resourceMonitor.MatcherSubscriptions.Single().Matcher;

        scope.ApplySearchExclude = true;
        var matcherWhileOn = _resourceMonitor.MatcherSubscriptions.Single().Matcher;

        matcherWhileOff.IsMatch("dist/app.js", isFolder: false).Should().BeTrue();
        matcherWhileOff.Should().NotBeSameAs(policy.SearchScope);
        matcherWhileOn.Should().BeSameAs(policy.SearchScope);
    }

    [Test]
    public void Subscription_FollowsEveryPath_InAFolderTheSearchListExcludes()
    {
        var policy = UseSearchExclude("node_modules");
        var scope = CreateScope();

        scope.SetFolder(new ResourceKey("node_modules/lib"));

        var matcher = _resourceMonitor.MatcherSubscriptions.Single().Matcher;
        matcher.Should().NotBeSameAs(policy.SearchScope);
        matcher.IsMatch("node_modules/lib/index.js", isFolder: false).Should().BeTrue();
    }

    [Test]
    public void SearchedFilesChanged_FollowsAnExcludedFolder_OnlyWithTheSearchListOff()
    {
        UseSearchExclude("dist");
        var scope = CreateScope();
        var changeCount = 0;
        scope.SearchedFilesChanged += (_, _) => changeCount++;

        _resourceMonitor.DeliverChange(new ResourceKey("dist/app.js"), ResourceItemState.File);
        var countWhileOn = changeCount;

        scope.ApplySearchExclude = false;
        _resourceMonitor.DeliverChange(new ResourceKey("dist/app.js"), ResourceItemState.File);

        countWhileOn.Should().Be(0);
        changeCount.Should().Be(1);
    }

    [Test]
    public async Task BrowseFolder_SetsThePickedFolder()
    {
        _dialogService.ShowFolderPickerDialogAsync(Arg.Any<string?>())
            .Returns(Result<ResourceKey>.Ok(new ResourceKey("docs")));
        var scope = CreateScope();

        await scope.BrowseFolderCommand.ExecuteAsync(null);

        scope.Folder.Should().Be(new ResourceKey("docs"));
    }

    [Test]
    public async Task BrowseFolder_PickingTheProjectFolder_SearchesTheWholeProject()
    {
        _workspaceSettings.SearchFolder = "project:docs";
        _dialogService.ShowFolderPickerDialogAsync(Arg.Any<string?>())
            .Returns(Result<ResourceKey>.Ok(ResourceKey.Empty));
        var scope = CreateScope();

        await scope.BrowseFolderCommand.ExecuteAsync(null);

        scope.Folder.IsEmpty.Should().BeTrue();
    }

    [Test]
    public async Task BrowseFolder_Dismissed_KeepsTheFolder()
    {
        _workspaceSettings.SearchFolder = "project:docs";
        _dialogService.ShowFolderPickerDialogAsync(Arg.Any<string?>())
            .Returns(Result<ResourceKey>.Fail("Dismissed"));
        var scope = CreateScope();

        await scope.BrowseFolderCommand.ExecuteAsync(null);

        scope.Folder.Should().Be(new ResourceKey("docs"));
    }

    [Test]
    public void ClearFolder_SearchesTheWholeProject()
    {
        _workspaceSettings.SearchFolder = "project:docs";
        var scope = CreateScope();

        scope.ClearFolderCommand.Execute(null);

        scope.Folder.IsEmpty.Should().BeTrue();
        _workspaceSettings.SearchFolder.Should().BeEmpty();
    }

    private SearchScopeViewModel CreateScope()
    {
        return new SearchScopeViewModel(_workspaceWrapper, _dialogService, _stringLocalizer);
    }

    private ResourcePolicy UseSearchExclude(params string[] patterns)
    {
        var policy = TestResourcePolicy.CreateWithSearchExclude(_projectFolderPath, patterns);
        _resourceService.Policy.Returns(policy);

        return policy;
    }

    private static ITextMatcher Matcher(string pattern)
    {
        var patternOptions = new SearchPatternOptions(
            Literal: true,
            MatchCase: false,
            WholeWord: false,
            Multiline: false);

        return SearchPattern.Compile(pattern, patternOptions).Value;
    }
}
