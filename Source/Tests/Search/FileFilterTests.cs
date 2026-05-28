using Celbridge.Messaging;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Search;
using Celbridge.Workspace;

namespace Celbridge.Tests.Search;

[TestFixture]
public class FileFilterTests
{
    private FileFilter _filter = null!;
    private IResourceFileSystem _fileSystem = null!;
    private IResourceRegistry _resourceRegistry = null!;
    private string _testDir = null!;

    [SetUp]
    public void SetUp()
    {
        _filter = new FileFilter(new TextBinarySniffer());
        _testDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDir);

        // Wire a real ResourceFileSystem so size + existence probes hit disk.
        _resourceRegistry = Substitute.For<IResourceRegistry>();
        _resourceRegistry.ProjectFolderPath.Returns(_testDir);

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(_resourceRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        _fileSystem = new ResourceFileSystem(
            Substitute.For<ILogger<ResourceFileSystem>>(),
            Substitute.For<IMessengerService>(),
            workspaceWrapper);
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

        (await _filter.ShouldSearchFileAsync(_fileSystem, resource, filePath)).Should().BeTrue();
    }

    [Test]
    public async Task ShouldSearchFile_NonExistentFile_ReturnsFalse()
    {
        var (resource, filePath) = MakeResource("nonexistent.txt");

        (await _filter.ShouldSearchFileAsync(_fileSystem, resource, filePath)).Should().BeFalse();
    }

    [Test]
    public async Task ShouldSearchFile_MetadataExtension_ReturnsFalse()
    {
        var (resource, filePath) = MakeResource("test.celbridge");
        File.WriteAllText(filePath, "metadata");

        (await _filter.ShouldSearchFileAsync(_fileSystem, resource, filePath)).Should().BeFalse();
    }

    [Test]
    public async Task ShouldSearchFile_CelExtension_ReturnsFalse()
    {
        // .cel files (sidecars and standalone forms such as .webview.cel) are
        // excluded from plain-text search because their content is editor-owned
        // and a plain-text replace would corrupt the file structure.
        var (resource, filePath) = MakeResource("test.webview.cel");
        File.WriteAllText(filePath, "source_url = \"https://example.com\"\n");

        (await _filter.ShouldSearchFileAsync(_fileSystem, resource, filePath)).Should().BeFalse();
    }

    [Test]
    public async Task ShouldSearchFile_BinaryExtension_ReturnsFalse()
    {
        var (resource, filePath) = MakeResource("test.exe");
        File.WriteAllBytes(filePath, new byte[] { 0x00, 0x01, 0x02 });

        (await _filter.ShouldSearchFileAsync(_fileSystem, resource, filePath)).Should().BeFalse();
    }

    [Test]
    public async Task ShouldSearchFile_ImageExtension_ReturnsFalse()
    {
        var (resource, filePath) = MakeResource("test.png");
        File.WriteAllBytes(filePath, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        (await _filter.ShouldSearchFileAsync(_fileSystem, resource, filePath)).Should().BeFalse();
    }

    [Test]
    public async Task ShouldSearchFile_CSharpFile_ReturnsTrue()
    {
        var (resource, filePath) = MakeResource("Test.cs");
        File.WriteAllText(filePath, "public class Test { }");

        (await _filter.ShouldSearchFileAsync(_fileSystem, resource, filePath)).Should().BeTrue();
    }

    [Test]
    public async Task ShouldSearchFile_MarkdownFile_ReturnsTrue()
    {
        var (resource, filePath) = MakeResource("README.md");
        File.WriteAllText(filePath, "# Readme");

        (await _filter.ShouldSearchFileAsync(_fileSystem, resource, filePath)).Should().BeTrue();
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

        (await _filter.ShouldSearchFileAsync(_fileSystem, resource, filePath)).Should().BeFalse();
    }

    [Test]
    public void IsTextContent_NormalText_ReturnsTrue()
    {
        var content = "This is normal text content";

        _filter.IsTextContent(content).Should().BeTrue();
    }

    [Test]
    public void IsTextContent_WithNullCharacter_ReturnsFalse()
    {
        var content = "Text with \0 null character";

        _filter.IsTextContent(content).Should().BeFalse();
    }

    [Test]
    public void IsTextContent_EmptyString_ReturnsTrue()
    {
        var content = "";

        _filter.IsTextContent(content).Should().BeTrue();
    }

    [Test]
    public void IsTextContent_Unicode_ReturnsTrue()
    {
        var content = "Text with Unicode: ñ, ü, 中文, 日本語, 한글";

        _filter.IsTextContent(content).Should().BeTrue();
    }
}
