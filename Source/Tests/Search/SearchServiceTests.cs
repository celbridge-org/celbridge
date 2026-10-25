using Celbridge.Automation;
using Celbridge.FileSystem.Services;
using Celbridge.Messaging.Services;
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

        var options = Options("hello") with { Include = "*.py,docs/" };
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

        var options = Options("hello") with { Include = "*.js", Exclude = "*.min.js" };
        var result = await _searchService.SearchAsync(options, CancellationToken.None);

        result.FileResults.Select(fileResult => fileResult.Resource).Should().Equal(new ResourceKey("src/app.js"));
    }

    [Test]
    public async Task SearchAsync_IncludeUnderAScope_IsRelativeToTheProject()
    {
        await WriteFileAsync("src/lib/util.py", "hello world");
        await WriteFileAsync("src/main.py", "hello world");

        var options = Options("hello", scope: "src") with { Include = "src/lib/**" };
        var result = await _searchService.SearchAsync(options, CancellationToken.None);

        result.FileResults.Select(fileResult => fileResult.Resource).Should().Equal(new ResourceKey("src/lib/util.py"));
    }

    [Test]
    public async Task SearchAsync_IncludeThatMatchesNoFile_ReportsNoFilesMatchingTheFilters()
    {
        await WriteFileAsync("src/app.py", "hello world");

        var options = Options("hello") with { Include = "*.cs" };
        var result = await _searchService.SearchAsync(options, CancellationToken.None);

        result.FileResults.Should().BeEmpty();
        result.FilesMatchingFilters.Should().Be(0);
    }

    [Test]
    public async Task SearchAsync_Regex_MatchesARipgrepPattern()
    {
        await WriteFileAsync("notes.txt", "alpha 12\nbeta\ngamma 3\n");

        var options = new SearchOptions(@"(?P<word>\w+) \d+", Literal: false, MatchCase: true, WholeWord: false);
        var result = await _searchService.SearchAsync(options, CancellationToken.None);

        result.TotalMatches.Should().Be(2);
    }

    [Test]
    public async Task SearchAsync_RefusedPattern_FindsNothing()
    {
        await WriteFileAsync("notes.txt", "alpha\n");

        var options = new SearchOptions("a(?=l)", Literal: false, MatchCase: true, WholeWord: false);
        var result = await _searchService.SearchAsync(options, CancellationToken.None);

        result.FileResults.Should().BeEmpty();
    }

    private static SearchOptions Options(string pattern, string scope = "", bool includeMetadataFiles = false)
    {
        return new SearchOptions(pattern, Literal: true, MatchCase: false, WholeWord: false)
        {
            Scope = scope,
            IncludeMetadataFiles = includeMetadataFiles,
        };
    }

    private async Task WriteFileAsync(string relativePath, string content)
    {
        var fullPath = Path.Combine(_tempFolder, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, content);
    }
}
