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
public class SearchServiceFilterTests
{
    private SearchService _service = null!;
    private IResourceRegistry _resourceRegistry = null!;
    private string _testDir = null!;

    [SetUp]
    public void SetUp()
    {
        _testDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDir);

        _resourceRegistry = Substitute.For<IResourceRegistry>();
        _resourceRegistry.ProjectFolderPath.Returns(_testDir);

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(_resourceRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);
        resourceService.Policy.Returns(TestResourcePolicy.CreateDefault());

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);
        workspaceWrapper.IsWorkspacePageLoaded.Returns(false);

        // Wire a real LocalResourceFileSystem so size + existence probes hit disk through the gateway.
        var resourceFileSystem = new LocalResourceFileSystem(
            Substitute.For<ILogger<LocalResourceFileSystem>>(),
            Substitute.For<IMessengerService>(),
            workspaceWrapper,
            TestFileSystem.CreateLocal());
        resourceService.FileSystem.Returns(resourceFileSystem);

        _service = new SearchService(
            Substitute.For<ILogger<SearchService>>(),
            workspaceWrapper,
            new TextBinarySniffer(new LocalFileSystem(MigrationTestHelper.CreateMockLogger<LocalFileSystem>())));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }
    }

    private (ResourceKey Resource, string Path) MakeResource(string name)
    {
        var resource = new ResourceKey(name);
        var path = Path.Combine(_testDir, name);
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));
        return (resource, path);
    }

    [Test]
    public async Task ShouldSearchFile_RegularTextFile_ReturnsTrue()
    {
        var (resource, filePath) = MakeResource("test.txt");
        File.WriteAllText(filePath, "test content");

        (await _service.ShouldSearchFileAsync(resource, filePath)).Should().BeTrue();
    }

    [Test]
    public async Task ShouldSearchFile_NonExistentFile_ReturnsFalse()
    {
        var (resource, filePath) = MakeResource("nonexistent.txt");

        (await _service.ShouldSearchFileAsync(resource, filePath)).Should().BeFalse();
    }

    [Test]
    public async Task ShouldSearchFile_MetadataExtension_ReturnsFalse()
    {
        var (resource, filePath) = MakeResource("test.celbridge");
        File.WriteAllText(filePath, "metadata");

        (await _service.ShouldSearchFileAsync(resource, filePath)).Should().BeFalse();
    }

    [Test]
    public async Task ShouldSearchFile_CelExtension_ReturnsFalse()
    {
        // .cel sidecar files are excluded from plain-text search because their
        // content is editor-owned and a plain-text replace would corrupt the
        // TOML frontmatter or block fences.
        var (resource, filePath) = MakeResource("photo.png.cel");
        File.WriteAllText(filePath, "tags = [\"x\"]\n");

        (await _service.ShouldSearchFileAsync(resource, filePath)).Should().BeFalse();
    }

    [Test]
    public async Task ShouldSearchFile_BinaryExtension_ReturnsFalse()
    {
        var (resource, filePath) = MakeResource("test.exe");
        File.WriteAllBytes(filePath, new byte[] { 0x00, 0x01, 0x02 });

        (await _service.ShouldSearchFileAsync(resource, filePath)).Should().BeFalse();
    }

    [Test]
    public async Task ShouldSearchFile_ImageExtension_ReturnsFalse()
    {
        var (resource, filePath) = MakeResource("test.png");
        File.WriteAllBytes(filePath, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        (await _service.ShouldSearchFileAsync(resource, filePath)).Should().BeFalse();
    }

    [Test]
    public async Task ShouldSearchFile_CSharpFile_ReturnsTrue()
    {
        var (resource, filePath) = MakeResource("Test.cs");
        File.WriteAllText(filePath, "public class Test { }");

        (await _service.ShouldSearchFileAsync(resource, filePath)).Should().BeTrue();
    }

    [Test]
    public async Task ShouldSearchFile_MarkdownFile_ReturnsTrue()
    {
        var (resource, filePath) = MakeResource("README.md");
        File.WriteAllText(filePath, "# Readme");

        (await _service.ShouldSearchFileAsync(resource, filePath)).Should().BeTrue();
    }

    [Test]
    public async Task ShouldSearchFile_LargeFile_ReturnsFalse()
    {
        var (resource, filePath) = MakeResource("large.txt");
        // Create a file larger than 1MB
        using (var fs = File.Create(filePath))
        {
            fs.SetLength(1024 * 1024 + 1);
        }

        (await _service.ShouldSearchFileAsync(resource, filePath)).Should().BeFalse();
    }
}
