using Celbridge.Browser.Services;
using Celbridge.FileSystem.Services;
using Celbridge.Projects;
using Celbridge.Tests.Helpers;
using Celbridge.Tests.Migration.TestHelpers;

namespace Celbridge.Tests.Browser;

[TestFixture]
public class BrowserFaviconCacheTests
{
    private string _dataFolderPath = null!;
    private BrowserFaviconCache _cache = null!;

    [SetUp]
    public void Setup()
    {
        _dataFolderPath = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(BrowserFaviconCacheTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataFolderPath);

        var project = Substitute.For<IProject>();
        project.ProjectDataFolderPath.Returns(_dataFolderPath);

        var projectService = Substitute.For<IProjectService>();
        projectService.CurrentProject.Returns(project);

        _cache = new BrowserFaviconCache(
            new NullLogger<BrowserFaviconCache>(),
            projectService,
            new LocalFileSystem(MigrationTestHelper.CreateMockLogger<LocalFileSystem>()));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dataFolderPath))
        {
            Directory.Delete(_dataFolderPath, recursive: true);
        }
    }

    [Test]
    public async Task TryReadBookmarkIconAsync_BeforeTheFaviconIsCached_IsNull()
    {
        (await _cache.TryReadBookmarkIconAsync("https://example.com/")).Should().BeNull();
    }

    [Test]
    public async Task WriteBookmarkIconAsync_StoresTheFaviconInTheFaviconsFolder()
    {
        var favicon = new byte[] { 1, 2, 3 };

        await _cache.WriteBookmarkIconAsync("https://example.com/", favicon);

        (await _cache.TryReadBookmarkIconAsync("https://example.com/")).Should().Equal(favicon);
        Directory.GetFiles(Path.Combine(_dataFolderPath, ProjectConstants.FaviconsFolder)).Should().ContainSingle();
    }

    [Test]
    public async Task AnAddressMissingItsTrailingSlash_SharesAFile()
    {
        var favicon = new byte[] { 1, 2, 3 };

        await _cache.WriteBookmarkIconAsync("https://example.com", favicon);

        (await _cache.TryReadBookmarkIconAsync("https://example.com/")).Should().Equal(favicon);
    }

    [Test]
    public async Task WriteIconAsync_StoresTheFileForItsAddress()
    {
        var favicon = new byte[] { 1, 2, 3 };

        await _cache.WriteIconAsync("https://example.com/favicon.ico", favicon);

        (await _cache.TryReadIconAsync("https://example.com/favicon.ico")).Should().Equal(favicon);
        (await _cache.TryReadIconAsync("https://example.com/other.ico")).Should().BeNull();
    }

    [Test]
    public async Task WriteIconAsync_ReplacesTheEarlierFile()
    {
        await _cache.WriteIconAsync("https://example.com/favicon.ico", new byte[] { 1 });
        await _cache.WriteIconAsync("https://example.com/favicon.ico", new byte[] { 2 });

        (await _cache.TryReadIconAsync("https://example.com/favicon.ico")).Should().Equal(2);
    }

    [Test]
    public async Task ABookmarkAndAnIconAtOneAddress_HaveSeparateFiles()
    {
        // A bookmark's file is the icon of the bookmark's page, which differs from the image at that address.
        const string address = "https://example.com/logo.png";

        await _cache.WriteBookmarkIconAsync(address, new byte[] { 1 });
        await _cache.WriteIconAsync(address, new byte[] { 2 });

        (await _cache.TryReadBookmarkIconAsync(address)).Should().Equal(1);
        (await _cache.TryReadIconAsync(address)).Should().Equal(2);
    }

    [Test]
    public void AFileName_ForAnAddressThatCannotBeNavigatedTo_IsNull()
    {
        BrowserFaviconCache.GetBookmarkFileName("file:///etc/passwd").Should().BeNull();
        BrowserFaviconCache.GetIconFileName("data:image/png;base64,AAAA").Should().BeNull();
    }
}
