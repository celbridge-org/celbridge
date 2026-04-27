using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Tests for ResourceFileWriter — atomic writes, retry on transient IO,
/// parent-folder creation, and ResolveResourcePath integration.
/// </summary>
[TestFixture]
public class ResourceFileWriterTests
{
    private string _tempFolder = null!;
    private IResourceRegistry _resourceRegistry = null!;
    private ResourceFileWriter _writer = null!;

    [SetUp]
    public void Setup()
    {
        _tempFolder = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(ResourceFileWriterTests),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);

        _resourceRegistry = Substitute.For<IResourceRegistry>();
        _resourceRegistry.ProjectFolderPath.Returns(_tempFolder);

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(_resourceRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        var workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        workspaceWrapper.WorkspaceService.Returns(workspaceService);

        _writer = new ResourceFileWriter(
            Substitute.For<ILogger<ResourceFileWriter>>(),
            workspaceWrapper);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempFolder))
        {
            Directory.Delete(_tempFolder, true);
        }
    }

    [Test]
    public async Task WriteAllBytesAsync_WritesContent_WhenFileDoesNotExist()
    {
        var resource = new ResourceKey("new.bin");
        var path = Path.Combine(_tempFolder, "new.bin");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var bytes = new byte[] { 0x01, 0x02, 0x03 };

        var result = await _writer.WriteAllBytesAsync(resource, bytes);

        result.IsSuccess.Should().BeTrue();
        File.Exists(path).Should().BeTrue();
        (await File.ReadAllBytesAsync(path)).Should().Equal(bytes);
    }

    [Test]
    public async Task WriteAllTextAsync_WritesContent_WhenFileDoesNotExist()
    {
        var resource = new ResourceKey("new.txt");
        var path = Path.Combine(_tempFolder, "new.txt");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var result = await _writer.WriteAllTextAsync(resource, "hello world");

        result.IsSuccess.Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be("hello world");
    }

    [Test]
    public async Task WriteAllTextAsync_OverwritesExistingFile()
    {
        var resource = new ResourceKey("existing.txt");
        var path = Path.Combine(_tempFolder, "existing.txt");
        await File.WriteAllTextAsync(path, "old");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var result = await _writer.WriteAllTextAsync(resource, "new");

        result.IsSuccess.Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be("new");
    }

    [Test]
    public async Task WriteAllTextAsync_CreatesIntermediateFolders()
    {
        var resource = new ResourceKey("nested/deeper/file.txt");
        var path = Path.Combine(_tempFolder, "nested", "deeper", "file.txt");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var result = await _writer.WriteAllTextAsync(resource, "deep content");

        result.IsSuccess.Should().BeTrue();
        File.Exists(path).Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be("deep content");
    }

    [Test]
    public async Task WriteAllTextAsync_ReturnsFailure_WhenResolveFails()
    {
        var resource = new ResourceKey("bad.txt");
        _resourceRegistry.ResolveResourcePath(resource)
            .Returns(Result<string>.Fail("simulated resolve failure"));

        var result = await _writer.WriteAllTextAsync(resource, "anything");

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task WriteAllBytesAsync_StagesTempInCelbridgeTempFolder_AndLeavesNoOrphan()
    {
        // Atomic writes stage temp files in <project>/celbridge/.temp/, not
        // alongside the destination. After a successful write the temp folder
        // exists (next caller may use it) but contains no leftover .tmp file.
        var resource = new ResourceKey("clean.bin");
        var path = Path.Combine(_tempFolder, "clean.bin");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        await _writer.WriteAllBytesAsync(resource, new byte[] { 0x42 });

        // No sibling temp file next to the destination.
        File.Exists(path + ".tmp").Should().BeFalse();

        // Central temp folder exists but is empty.
        var centralTempFolder = Path.Combine(_tempFolder, ProjectConstants.MetaDataFolder, ProjectConstants.TempFolder);
        Directory.Exists(centralTempFolder).Should().BeTrue();
        Directory.GetFiles(centralTempFolder).Should().BeEmpty();
    }
}
