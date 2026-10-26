using Celbridge.Documents;
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
/// Tests for the Search panel's filters: the files to include and to exclude, Search Only in Open Documents and Apply
/// Search Exclude Settings. Covers the workspace settings, the search options, Search in Folder and the dot on the
/// filters button.
/// </summary>
[TestFixture]
public class SearchFiltersViewModelTests
{
    private IResourceService _resourceService = null!;
    private IDocumentsService _documentsService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private IBindableWorkspaceSettings _workspaceSettings = null!;
    private IStringLocalizer _stringLocalizer = null!;

    [SetUp]
    public void Setup()
    {
        _resourceService = Substitute.For<IResourceService>();
        _resourceService.Policy.Returns(TestResourcePolicy.CreateDefault());

        _documentsService = Substitute.For<IDocumentsService>();

        // A substitute's settable properties return the value last set, the way the settings store does.
        _workspaceSettings = Substitute.For<IBindableWorkspaceSettings>();
        _workspaceSettings.ApplySearchExclude = true;

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(_resourceService);
        workspaceService.DocumentsService.Returns(_documentsService);
        workspaceService.BindableWorkspaceSettings.Returns(_workspaceSettings);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.IsWorkspaceLoaded.Returns(true);
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        _stringLocalizer = Substitute.For<IStringLocalizer>();
        _stringLocalizer[Arg.Any<string>()].Returns(call =>
        {
            var name = call.Arg<string>();
            return new LocalizedString(name, name);
        });
    }

    [Test]
    public void Constructor_ReadsTheFiltersFromTheWorkspaceSettings()
    {
        _workspaceSettings.SearchFiltersVisible = true;
        _workspaceSettings.SearchFilesToInclude = "./docs";
        _workspaceSettings.SearchFilesToExclude = "drafts/";
        _workspaceSettings.SearchOnlyOpenDocuments = true;
        _workspaceSettings.ApplySearchExclude = false;

        var filters = CreateFilters();

        filters.AreFiltersVisible.Should().BeTrue();
        filters.FilesToInclude.Should().Be("./docs");
        filters.FilesToExclude.Should().Be("drafts/");
        filters.SearchOnlyOpenDocuments.Should().BeTrue();
        filters.ApplySearchExclude.Should().BeFalse();
    }

    [Test]
    public void Changes_AreKeptInTheWorkspaceSettings()
    {
        var filters = CreateFilters();

        filters.AreFiltersVisible = true;
        filters.FilesToInclude = "*.md";
        filters.FilesToExclude = "drafts/";
        filters.SearchOnlyOpenDocuments = true;
        filters.ApplySearchExclude = false;

        _workspaceSettings.SearchFiltersVisible.Should().BeTrue();
        _workspaceSettings.SearchFilesToInclude.Should().Be("*.md");
        _workspaceSettings.SearchFilesToExclude.Should().Be("drafts/");
        _workspaceSettings.SearchOnlyOpenDocuments.Should().BeTrue();
        _workspaceSettings.ApplySearchExclude.Should().BeFalse();
    }

    [Test]
    public void Changed_IsTypedForAField_AndNotForAToggle()
    {
        var filters = CreateFilters();
        var changes = new List<SearchFiltersChange>();
        filters.Changed += (_, change) => changes.Add(change);

        filters.FilesToInclude = "*.md";
        filters.FilesToExclude = "drafts/";
        filters.SearchOnlyOpenDocuments = true;
        filters.ApplySearchExclude = false;
        filters.AreFiltersVisible = true;

        changes.Select(change => change.IsTyped).Should().Equal(true, true, false, false);
    }

    [Test]
    public void CreateSearchOptions_CarriesTheFieldsAndApplySearchExclude()
    {
        var filters = CreateFilters();
        filters.FilesToInclude = "./docs, *.md";
        filters.FilesToExclude = "drafts/";
        filters.ApplySearchExclude = false;

        var options = filters.CreateSearchOptions(Matcher("hello"), maxResults: 1000);

        options.Should().NotBeNull();
        options!.Matcher.Pattern.Should().Be("hello");
        options.Scope.Should().BeEmpty();
        options.Files.Should().BeNull();
        options.MaxResults.Should().Be(1000);
        options.ApplySearchExclude.Should().BeFalse();
        options.Filter.IsMatch("docs/guide.txt", isFolder: false).Should().BeTrue();
        options.Filter.IsMatch("src/docs/guide.txt", isFolder: false).Should().BeFalse();
        options.Filter.IsMatch("src/notes.md", isFolder: false).Should().BeTrue();
        options.Filter.IsMatch("docs/drafts/guide.md", isFolder: false).Should().BeFalse();
    }

    [Test]
    public void CreateSearchOptions_ForReplaceAll_HasNoCap()
    {
        var filters = CreateFilters();

        var options = filters.CreateSearchOptions(Matcher("hello"), maxResults: null);

        options.Should().NotBeNull();
        options!.MaxResults.Should().BeNull();
        options.ApplySearchExclude.Should().BeTrue();
    }

    [Test]
    public void CreateSearchOptions_WithOnlyOpenDocuments_NamesTheOpenDocumentsFiles()
    {
        _documentsService.GetOpenDocuments().Returns(
        [
            new OpenDocumentInfo(new ResourceKey("docs/guide.md"), default!, default!),
            new OpenDocumentInfo(new ResourceKey("notes.txt"), default!, default!),
        ]);
        var filters = CreateFilters();

        filters.SearchOnlyOpenDocuments = true;
        var options = filters.CreateSearchOptions(Matcher("hello"), maxResults: 1000);

        options!.Files.Should().Equal(new ResourceKey("docs/guide.md"), new ResourceKey("notes.txt"));
    }

    [Test]
    public void RefusedFilter_ShowsTheInvalidLine_AndStopsTheSearch()
    {
        var filters = CreateFilters();

        filters.FilesToExclude = "*.{md";
        var refusedOptions = filters.CreateSearchOptions(Matcher("hello"), maxResults: 1000);
        var refusedLine = filters.HasInvalidFilter;

        filters.FilesToExclude = "*.{md,txt}";
        var acceptedOptions = filters.CreateSearchOptions(Matcher("hello"), maxResults: 1000);

        refusedLine.Should().BeTrue();
        refusedOptions.Should().BeNull();
        filters.HasInvalidFilter.Should().BeFalse();
        acceptedOptions.Should().NotBeNull();
    }

    [Test]
    public void SetFolder_WritesTheFolderIntoTheFilesToInclude_AndShowsTheFilters()
    {
        var filters = CreateFilters();
        var changes = new List<SearchFiltersChange>();
        filters.Changed += (_, change) => changes.Add(change);

        filters.SetFolder(new ResourceKey("docs/api"));

        filters.FilesToInclude.Should().Be("./docs/api");
        filters.AreFiltersVisible.Should().BeTrue();
        filters.ApplySearchExclude.Should().BeTrue();
        changes.Should().BeEmpty("the caller starts the search");
    }

    [Test]
    public void SetFolder_EscapesThePatternCharactersInTheFolderName()
    {
        var filters = CreateFilters();

        filters.SetFolder(new ResourceKey("notes, {old} [1]"));
        var options = filters.CreateSearchOptions(Matcher("hello"), maxResults: 1000);

        filters.FilesToInclude.Should().Be(@"./notes\, \{old\} \[1\]");
        options!.Filter.IsMatch("notes, {old} [1]/todo.txt", isFolder: false).Should().BeTrue();
        options.Filter.IsMatch("notes/todo.txt", isFolder: false).Should().BeFalse();
    }

    [Test]
    public void SetFolder_OnTheProjectFolder_ClearsTheFilesToInclude()
    {
        _workspaceSettings.SearchFilesToInclude = "./docs";
        var filters = CreateFilters();

        filters.SetFolder(ResourceKey.Empty);

        filters.FilesToInclude.Should().BeEmpty();
        filters.AreFiltersVisible.Should().BeTrue();
    }

    [Test]
    public void SetFolder_LeavesTheTogglesAsTheUserSetThem()
    {
        UseSearchExclude("node_modules");
        var filters = CreateFilters();
        filters.SearchOnlyOpenDocuments = true;

        filters.SetFolder(new ResourceKey("node_modules/lib"));
        var applyAfterAnExcludedFolder = filters.ApplySearchExclude;
        filters.ApplySearchExclude = false;
        filters.SetFolder(new ResourceKey("src"));

        applyAfterAnExcludedFolder.Should().BeTrue("Search in Folder sets only the files to include");
        filters.ApplySearchExclude.Should().BeFalse();
        filters.SearchOnlyOpenDocuments.Should().BeTrue();
    }

    [Test]
    public void Dot_ShowsWhileTheFiltersAreHiddenAndInForce()
    {
        var filters = CreateFilters();
        var dotWithNoFilter = filters.ShowHiddenFiltersDot;

        filters.FilesToExclude = "drafts/";
        var dotWhileHidden = filters.ShowHiddenFiltersDot;

        filters.AreFiltersVisible = true;
        var dotWhileShown = filters.ShowHiddenFiltersDot;

        dotWithNoFilter.Should().BeFalse();
        dotWhileHidden.Should().BeTrue();
        dotWhileShown.Should().BeFalse();
    }

    [TestCase(true, true)]
    [TestCase(false, false)]
    public void Dot_ShowsForAToggleAwayFromItsDefault(bool searchOnlyOpenDocuments, bool applySearchExclude)
    {
        var filters = CreateFilters();

        filters.SearchOnlyOpenDocuments = searchOnlyOpenDocuments;
        filters.ApplySearchExclude = applySearchExclude;

        filters.ShowHiddenFiltersDot.Should().BeTrue();
    }

    [Test]
    public void Dot_StaysOff_ForWhitespaceFields()
    {
        var filters = CreateFilters();

        filters.FilesToInclude = "  ";

        filters.ShowHiddenFiltersDot.Should().BeFalse();
    }

    private SearchFiltersViewModel CreateFilters()
    {
        return new SearchFiltersViewModel(_workspaceWrapper, _stringLocalizer);
    }

    private ResourcePolicy UseSearchExclude(params string[] patterns)
    {
        var policy = TestResourcePolicy.CreateWithSearchExclude(Path.GetTempPath(), patterns);
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
