using Celbridge.FileSystem;
using Celbridge.FileSystem.Services;
using Celbridge.Messaging;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Search.Services;
using Celbridge.Tests.FileSystem;
using Celbridge.Tests.Migration.TestHelpers;
using Celbridge.Workspace;

namespace Celbridge.Tests.Search;

[TestFixture]
public class SearchServiceTests
{
    private string _tempFolder = null!;
    private IResourceRegistry _resourceRegistry = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;
    private SearchService _searchService = null!;

    [SetUp]
    public void SetUp()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(SearchServiceTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);

        _resourceRegistry = Substitute.For<IResourceRegistry>();
        _resourceRegistry.ProjectFolderPath.Returns(_tempFolder);
        _resourceRegistry.GetAllFileResources().Returns(new List<FileResourceEntry>());

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(_resourceRegistry);
        resourceService.Policy.Returns(TestResourcePolicy.CreateDefault());

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        // Guard.IsFalse(IsWorkspacePageLoaded) in the SearchService constructor requires false at creation time.
        // NSubstitute returns false for bool by default, but we set it explicitly for clarity.
        _workspaceWrapper.IsWorkspacePageLoaded.Returns(false);

        // A real LocalResourceFileSystem against the temp folder so tests that
        // place files under _tempFolder can be probed and read end-to-end.
        var resourceFileSystem = new LocalResourceFileSystem(
            Substitute.For<ILogger<LocalResourceFileSystem>>(),
            Substitute.For<IMessengerService>(),
            _workspaceWrapper,
            TestFileSystem.CreateLocal());
        resourceService.FileSystem.Returns(resourceFileSystem);

        _searchService = new SearchService(
            Substitute.For<ILogger<SearchService>>(),
            _workspaceWrapper,
            new TextBinarySniffer(new LocalFileSystem(MigrationTestHelper.CreateMockLogger<LocalFileSystem>())));

        // After construction, the workspace is "loaded" so SearchAsync proceeds past its early return.
        _workspaceWrapper.IsWorkspacePageLoaded.Returns(true);
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

    private async Task WriteFileAsync(string relativePath, string content)
    {
        var fullPath = Path.Combine(_tempFolder, relativePath);
        var folder = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }
        await File.WriteAllTextAsync(fullPath, content);

        var resourceKey = new ResourceKey(relativePath.Replace('\\', '/'));
        var entry = new FileResourceEntry(resourceKey, fullPath);

        var entries = _resourceRegistry.GetAllFileResources();
        var updated = entries.Concat(new[] { entry }).ToList();
        _resourceRegistry.GetAllFileResources().Returns(updated);

        // LocalResourceFileSystem.OpenReadAsync resolves the key through the
        // registry before opening the stream; without this the gateway read
        // fails and the file is silently skipped.
        _resourceRegistry.ResolveResourcePath(resourceKey).Returns(Result<string>.Ok(fullPath));
        _resourceRegistry.ResolveResourcePath(resourceKey, Arg.Any<bool>()).Returns(Result<string>.Ok(fullPath));
    }
}
