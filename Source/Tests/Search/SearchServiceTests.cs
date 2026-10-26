using Celbridge.Automation;
using Celbridge.FileSystem.Services;
using Celbridge.Messaging.Services;
using Celbridge.Patterns;
using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Search;
using Celbridge.Search.Services;
using Celbridge.Tests.FileSystem;
using Celbridge.Tests.Packages;
using Celbridge.Tests.Migration.TestHelpers;
using Celbridge.Workspace;

namespace Celbridge.Tests.Search;

/// <summary>
/// Tests for SearchService over a temporary project folder, walked through real root handlers and a real gateway.
/// </summary>
[TestFixture]
public class SearchServiceTests
{
    private string _tempFolder = null!;
    private IResourceService _resourceService = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private SearchService _searchService = null!;
    private IAutomationService _automationService = null!;

    [SetUp]
    public void SetUp()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(SearchServiceTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);

        var messengerService = new MessengerService();

        var resourceService = Substitute.For<IResourceService>();
        resourceService.RootHandlers.Returns(TestRootHandlers.Create(_tempFolder));
        resourceService.Policy.Returns(TestResourcePolicy.CreateDefault());
        _resourceService = resourceService;

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        // Guard.IsFalse(IsWorkspaceLoaded) in the SearchService constructor requires false at creation time.
        // NSubstitute returns false for bool by default, but we set it explicitly for clarity.
        _workspaceWrapper.IsWorkspaceLoaded.Returns(false);

        var resourceFileSystem = new LocalResourceFileSystem(
            Substitute.For<ILogger<LocalResourceFileSystem>>(),
            messengerService,
            _workspaceWrapper,
            TestFileSystem.CreateLocal());
        resourceService.FileSystem.Returns(resourceFileSystem);

        var walker = new ResourceWalker(Substitute.For<ILogger<ResourceWalker>>(), _workspaceWrapper);
        resourceService.Walker.Returns(walker);

        _automationService = Substitute.For<IAutomationService>();

        var project = Substitute.For<IProject>();
        project.ProjectFolderPath.Returns(_tempFolder);
        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns(project);

        _searchService = new SearchService(
            Substitute.For<ILogger<SearchService>>(),
            _workspaceWrapper,
            projectService,
            TestFileTypeCatalog.CreateSniffer(new LocalFileSystem(MigrationTestHelper.CreateMockLogger<LocalFileSystem>())),
            Substitute.For<Celbridge.UserInterface.Services.ISpotlightService>(),
            _automationService);

        // After construction, the workspace is "loaded" so SearchAsync proceeds past its early return.
        _workspaceWrapper.IsWorkspaceLoaded.Returns(true);
    }

    [TearDown]
    public void TearDown()
    {
        _searchService.Dispose();

        if (Directory.Exists(_tempFolder))
        {
            Directory.Delete(_tempFolder, true);
        }
    }

    [Test]
    public async Task SearchAsync_CancelledToken_ReturnsCancelledResult()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        var result = await _searchService.SearchAsync(Options("hello"), cancellationTokenSource.Token);

        result.WasCancelled.Should().BeTrue();
    }

    [Test]
    public async Task SearchAsync_TokenNotCancelled_ReturnsNotCancelled()
    {
        using var cancellationTokenSource = new CancellationTokenSource();

        var result = await _searchService.SearchAsync(Options("hello"), cancellationTokenSource.Token);

        result.WasCancelled.Should().BeFalse();
    }

    [Test]
    public async Task SearchAsync_AfterWorkspaceTeardown_StopsRatherThanReachingForTheWorkspace()
    {
        // A project reload disposes the workspace-scoped services before the wrapper drops its
        // reference. A search in flight at that moment must stop, not resume into a workspace that is
        // about to be gone.
        await WriteFileAsync("notes.txt", "hello world");

        _searchService.Dispose();

        var result = await _searchService.SearchAsync(Options("hello"), CancellationToken.None);

        result.WasCancelled.Should().BeTrue();
        result.FileResults.Should().BeEmpty();
    }

    [Test]
    public async Task SearchAsync_DefaultExcludesCelContent()
    {
        await WriteFileAsync("photo.png.cel", "title: \"sunset photo\"\n");

        var result = await _searchService.SearchAsync(Options("sunset"), CancellationToken.None);

        result.FileResults.Should().BeEmpty();
    }

    [Test]
    public async Task SearchAsync_IncludeMetadataFiles_MatchesCelContent()
    {
        await WriteFileAsync("photo.png.cel", "title: \"sunset photo\"\n");

        var result = await _searchService.SearchAsync(Options("sunset", includeMetadataFiles: true), CancellationToken.None);

        result.FileResults.Should().HaveCount(1);
        result.FileResults[0].Resource.ToString().Should().EndWith("photo.png.cel");
    }

    [Test]
    public async Task SearchAsync_Finished_ReportsTheFilesItSearched()
    {
        await WriteFileAsync("notes.txt", "hello world");
        await WriteFileAsync("other.txt", "goodbye");

        await _searchService.SearchAsync(Options("hello"), CancellationToken.None);

        _automationService.Received(1).LogEvent(Arg.Is<SearchCompletedEvent>(completed => completed.Files == 2));
    }

    [Test]
    public async Task SearchAsync_Cancelled_ReportsNoCompletion()
    {
        await WriteFileAsync("notes.txt", "hello world");

        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await _searchService.SearchAsync(Options("hello"), cancellationTokenSource.Token);

        _automationService.DidNotReceive().LogEvent(Arg.Any<SearchCompletedEvent>());
    }

    [Test]
    public async Task SearchAsync_ScopedToAFolder_SearchesOnlyWhatTheFolderContains()
    {
        await WriteFileAsync("src/app.py", "hello world");
        await WriteFileAsync("src2/app.py", "hello world");

        var result = await _searchService.SearchAsync(Options("hello", scope: "src"), CancellationToken.None);

        result.FileResults.Select(fileResult => fileResult.Resource).Should().Equal(new ResourceKey("src/app.py"));
    }

    [Test]
    public async Task SearchAsync_ScopedToAFile_SearchesThatFile()
    {
        await WriteFileAsync("src/app.py", "hello world");
        await WriteFileAsync("src/other.py", "hello world");

        var result = await _searchService.SearchAsync(Options("hello", scope: "src/app.py"), CancellationToken.None);

        result.FileResults.Select(fileResult => fileResult.Resource).Should().Equal(new ResourceKey("src/app.py"));
    }

    [Test]
    public async Task SearchAsync_Include_SearchesOnlyTheFilesItMatches()
    {
        await WriteFileAsync("src/app.py", "hello world");
        await WriteFileAsync("src/app.js", "hello world");
        await WriteFileAsync("docs/guide.md", "hello world");

        var options = Options("hello", include: "*.py,docs/");
        var result = await _searchService.SearchAsync(options, CancellationToken.None);

        var resources = result.FileResults.Select(fileResult => fileResult.Resource);
        resources.Should().BeEquivalentTo([new ResourceKey("docs/guide.md"), new ResourceKey("src/app.py")]);
        result.FilesMatchingFilters.Should().Be(2);
    }

    [Test]
    public async Task SearchAsync_Exclude_WinsOverInclude()
    {
        await WriteFileAsync("src/app.js", "hello world");
        await WriteFileAsync("src/app.min.js", "hello world");

        var options = Options("hello", include: "*.js", exclude: "*.min.js");
        var result = await _searchService.SearchAsync(options, CancellationToken.None);

        result.FileResults.Select(fileResult => fileResult.Resource).Should().Equal(new ResourceKey("src/app.js"));
    }

    [Test]
    public async Task SearchAsync_IncludeUnderAScope_IsRelativeToTheProject()
    {
        await WriteFileAsync("src/lib/util.py", "hello world");
        await WriteFileAsync("src/main.py", "hello world");

        var options = Options("hello", scope: "src", include: "src/lib/**");
        var result = await _searchService.SearchAsync(options, CancellationToken.None);

        result.FileResults.Select(fileResult => fileResult.Resource).Should().Equal(new ResourceKey("src/lib/util.py"));
    }

    [Test]
    public async Task SearchAsync_IncludeThatMatchesNoFile_ReportsNoFilesMatchingTheFilters()
    {
        await WriteFileAsync("src/app.py", "hello world");

        var options = Options("hello", include: "*.cs");
        var result = await _searchService.SearchAsync(options, CancellationToken.None);

        result.FileResults.Should().BeEmpty();
        result.FilesMatchingFilters.Should().Be(0);
    }

    [Test]
    public async Task SearchAsync_Regex_MatchesARipgrepPattern()
    {
        await WriteFileAsync("notes.txt", "alpha 12\nbeta\ngamma 3\n");

        var patternOptions = new SearchPatternOptions(Literal: false, MatchCase: true, WholeWord: false, Multiline: false);
        var matcher = SearchPattern.Compile(@"(?P<word>\w+) \d+", patternOptions).Value;
        var options = new SearchOptions(matcher, SearchFilter.Compile(string.Empty, string.Empty).Value);
        var result = await _searchService.SearchAsync(options, CancellationToken.None);

        result.TotalMatches.Should().Be(2);
        result.SearchTerm.Should().Be(@"(?P<word>\w+) \d+");
    }

    [Test]
    public async Task SearchAsync_LeavesOutWhatTheSearchListExcludes()
    {
        await WriteFileAsync("src/app.js", "hello world");
        await WriteFileAsync("dist/app.js", "hello world");
        UseSearchExclude("dist");

        var result = await _searchService.SearchAsync(Options("hello"), CancellationToken.None);

        result.FileResults.Select(fileResult => fileResult.Resource).Should().Equal(new ResourceKey("src/app.js"));
    }

    [Test]
    public async Task SearchAsync_ApplySearchExcludeOff_SearchesWhatTheListExcludes()
    {
        await WriteFileAsync("src/app.js", "hello world");
        await WriteFileAsync("dist/app.js", "hello world");
        UseSearchExclude("dist");

        var options = Options("hello") with { ApplySearchExclude = false };
        var result = await _searchService.SearchAsync(options, CancellationToken.None);

        var resources = result.FileResults.Select(fileResult => fileResult.Resource);
        resources.Should().Equal(new ResourceKey("dist/app.js"), new ResourceKey("src/app.js"));
    }

    [Test]
    public async Task SearchAsync_ScopedToAnExcludedFolder_SearchesTheFolderInFull()
    {
        await WriteFileAsync("node_modules/lib/index.js", "hello world");
        await WriteFileAsync("src/app.js", "hello world");
        UseSearchExclude("node_modules");

        var result = await _searchService.SearchAsync(Options("hello", scope: "node_modules/lib"), CancellationToken.None);

        result.FileResults.Select(fileResult => fileResult.Resource).Should().Equal(new ResourceKey("node_modules/lib/index.js"));
    }

    [Test]
    public async Task SearchAsync_WithNoCap_FindsEveryMatchInTheFolderAndFilters()
    {
        var manyMatches = string.Join("\n", Enumerable.Repeat("hello", 1500));
        await WriteFileAsync("docs/guide.md", manyMatches);
        await WriteFileAsync("docs/notes.txt", manyMatches);
        await WriteFileAsync("other/guide.md", manyMatches);

        var cappedOptions = Options("hello", scope: "docs", include: "*.md") with { MaxResults = 1000 };
        var cappedResult = await _searchService.SearchAsync(cappedOptions, CancellationToken.None);
        var allOptions = Options("hello", scope: "docs", include: "*.md");
        var allResult = await _searchService.SearchAsync(allOptions, CancellationToken.None);

        cappedResult.TotalMatches.Should().Be(1000);
        allResult.TotalMatches.Should().Be(1500);
        allResult.FileResults.Select(fileResult => fileResult.Resource).Should().Equal(new ResourceKey("docs/guide.md"));
    }

    private static SearchOptions Options(
        string pattern,
        string scope = "",
        bool includeMetadataFiles = false,
        string include = "",
        string exclude = "")
    {
        var patternOptions = new SearchPatternOptions(Literal: true, MatchCase: false, WholeWord: false, Multiline: false);
        var matcher = SearchPattern.Compile(pattern, patternOptions).Value;
        var filter = SearchFilter.Compile(include, exclude).Value;

        return new SearchOptions(matcher, filter)
        {
            Scope = scope,
            IncludeMetadataFiles = includeMetadataFiles,
        };
    }

    private void UseSearchExclude(params string[] patterns)
    {
        var policy = TestResourcePolicy.CreateWithSearchExclude(_tempFolder, patterns);
        _resourceService.Policy.Returns(policy);
    }

    private async Task WriteFileAsync(string relativePath, string content)
    {
        var fullPath = Path.Combine(_tempFolder, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, content);
    }
}
