using Celbridge.Automation;
using Celbridge.FileSystem.Services;
using Celbridge.Messaging.Services;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Search.Services;
using Celbridge.Tests.FileSystem;
using Celbridge.Tests.Packages;
using Celbridge.Tests.Migration.TestHelpers;
using Celbridge.Tests.Resources;
using Celbridge.UserInterface.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.Search;

/// <summary>
/// Tests for SearchService over a temporary project folder, walked through a real registry and gateway.
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
        var rootHandlerRegistry = new RootHandlerRegistry();
        var resourceRegistry = new ResourceRegistry(
            Substitute.For<ILogger<ResourceRegistry>>(),
            messengerService,
            ProjectTreeBuilderTestHelper.Build(_tempFolder, new IconService()),
            ResourceClassifierTestHelper.BuildClassifier(),
            rootHandlerRegistry,
            TestFileSystem.CreateLocal());
        resourceRegistry.InitializeProjectRoot(_tempFolder);

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(resourceRegistry);
        resourceService.RootHandlers.Returns(rootHandlerRegistry);
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

        _searchService = new SearchService(
            Substitute.For<ILogger<SearchService>>(),
            _workspaceWrapper,
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

        var result = await _searchService.SearchAsync(
            "hello",
            matchCase: false,
            wholeWord: false,
            maxResults: null,
            cancellationTokenSource.Token);

        result.WasCancelled.Should().BeTrue();
    }

    [Test]
    public async Task SearchAsync_TokenNotCancelled_ReturnsNotCancelled()
    {
        using var cancellationTokenSource = new CancellationTokenSource();

        var result = await _searchService.SearchAsync(
            "hello",
            matchCase: false,
            wholeWord: false,
            maxResults: null,
            cancellationTokenSource.Token);

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

        var result = await _searchService.SearchAsync(
            "hello",
            matchCase: false,
            wholeWord: false,
            maxResults: null,
            CancellationToken.None);

        result.WasCancelled.Should().BeTrue();
        result.FileResults.Should().BeEmpty();
    }

    [Test]
    public async Task SearchAsync_DefaultExcludesCelContent()
    {
        await WriteFileAsync("photo.png.cel", "title: \"sunset photo\"\n");

        var result = await _searchService.SearchAsync(
            "sunset",
            matchCase: false,
            wholeWord: false,
            maxResults: null,
            CancellationToken.None);

        result.FileResults.Should().BeEmpty();
    }

    [Test]
    public async Task SearchAsync_IncludeMetadataFiles_MatchesCelContent()
    {
        await WriteFileAsync("photo.png.cel", "title: \"sunset photo\"\n");

        var result = await _searchService.SearchAsync(
            "sunset",
            matchCase: false,
            wholeWord: false,
            maxResults: null,
            CancellationToken.None,
            includeMetadataFiles: true);

        result.FileResults.Should().HaveCount(1);
        result.FileResults[0].Resource.ToString().Should().EndWith("photo.png.cel");
    }

    [Test]
    public async Task SearchAsync_Finished_ReportsTheFilesItSearched()
    {
        await WriteFileAsync("notes.txt", "hello world");
        await WriteFileAsync("other.txt", "goodbye");

        await _searchService.SearchAsync(
            "hello",
            matchCase: false,
            wholeWord: false,
            maxResults: null,
            CancellationToken.None);

        _automationService.Received(1).LogEvent(Arg.Is<SearchCompletedEvent>(completed => completed.Files == 2));
    }

    [Test]
    public async Task SearchAsync_Cancelled_ReportsNoCompletion()
    {
        await WriteFileAsync("notes.txt", "hello world");

        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await _searchService.SearchAsync(
            "hello",
            matchCase: false,
            wholeWord: false,
            maxResults: null,
            cancellationTokenSource.Token);

        _automationService.DidNotReceive().LogEvent(Arg.Any<SearchCompletedEvent>());
    }

    [Test]
    public async Task SearchAsync_ScopedToAFolder_SearchesOnlyWhatTheFolderContains()
    {
        await WriteFileAsync("src/app.py", "hello world");
        await WriteFileAsync("src2/app.py", "hello world");

        var result = await _searchService.SearchAsync(
            "hello",
            matchCase: false,
            wholeWord: false,
            maxResults: null,
            CancellationToken.None,
            scope: "src");

        result.FileResults.Select(fileResult => fileResult.Resource).Should().Equal(new ResourceKey("src/app.py"));
    }

    [Test]
    public async Task SearchAsync_ScopedToAFile_SearchesThatFile()
    {
        await WriteFileAsync("src/app.py", "hello world");
        await WriteFileAsync("src/other.py", "hello world");

        var result = await _searchService.SearchAsync(
            "hello",
            matchCase: false,
            wholeWord: false,
            maxResults: null,
            CancellationToken.None,
            scope: "src/app.py");

        result.FileResults.Select(fileResult => fileResult.Resource).Should().Equal(new ResourceKey("src/app.py"));
    }

    private async Task WriteFileAsync(string relativePath, string content)
    {
        var fullPath = Path.Combine(_tempFolder, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, content);
    }
}
