using Celbridge.Projects;
using Celbridge.Resources;
using Celbridge.Resources.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Tests for ResourceFileSystem — atomic writes, retry on transient IO,
/// parent-folder creation, ResolveResourcePath integration, reads, and
/// stream-open happy paths.
/// </summary>
[TestFixture]
public class ResourceFileSystemTests
{
    private string _tempFolder = null!;
    private IResourceRegistry _resourceRegistry = null!;
    private ResourceFileSystem _fileSystem = null!;

    [SetUp]
    public void Setup()
    {
        _tempFolder = Path.Combine(
            Path.GetTempPath(),
            "Celbridge",
            nameof(ResourceFileSystemTests),
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

        _fileSystem = new ResourceFileSystem(
            Substitute.For<ILogger<ResourceFileSystem>>(),
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

        var result = await _fileSystem.WriteAllBytesAsync(resource, bytes);

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

        var result = await _fileSystem.WriteAllTextAsync(resource, "hello world");

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

        var result = await _fileSystem.WriteAllTextAsync(resource, "new");

        result.IsSuccess.Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be("new");
    }

    [Test]
    public async Task WriteAllTextAsync_CreatesIntermediateFolders()
    {
        var resource = new ResourceKey("nested/deeper/file.txt");
        var path = Path.Combine(_tempFolder, "nested", "deeper", "file.txt");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var result = await _fileSystem.WriteAllTextAsync(resource, "deep content");

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

        var result = await _fileSystem.WriteAllTextAsync(resource, "anything");

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task WriteAllBytesAsync_StagesTempInCelbridgeStagingFolder_AndLeavesNoOrphan()
    {
        // Atomic writes stage temp files in <project>/.celbridge/staging-fs/, not
        // alongside the destination. After a successful write the staging folder
        // exists (next caller may use it) but contains no leftover .tmp file.
        var resource = new ResourceKey("clean.bin");
        var path = Path.Combine(_tempFolder, "clean.bin");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        await _fileSystem.WriteAllBytesAsync(resource, new byte[] { 0x42 });

        // No sibling temp file next to the destination.
        File.Exists(path + ".tmp").Should().BeFalse();

        // Central staging folder exists but is empty.
        var stagingFolder = Path.Combine(
            _tempFolder,
            ProjectConstants.CelbridgeFolder,
            ProjectConstants.CelbridgeStagingFsFolder);
        Directory.Exists(stagingFolder).Should().BeTrue();
        Directory.GetFiles(stagingFolder).Should().BeEmpty();
    }

    [Test]
    public async Task ReadAllBytesAsync_ReturnsContent_WhenFileExists()
    {
        var resource = new ResourceKey("read.bin");
        var path = Path.Combine(_tempFolder, "read.bin");
        var expected = new byte[] { 0x10, 0x20, 0x30 };
        await File.WriteAllBytesAsync(path, expected);
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var result = await _fileSystem.ReadAllBytesAsync(resource);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Equal(expected);
    }

    [Test]
    public async Task ReadAllTextAsync_ReturnsContent_WhenFileExists()
    {
        var resource = new ResourceKey("read.txt");
        var path = Path.Combine(_tempFolder, "read.txt");
        await File.WriteAllTextAsync(path, "the content");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var result = await _fileSystem.ReadAllTextAsync(resource);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("the content");
    }

    [Test]
    public async Task ReadAllBytesAsync_ReturnsFailure_WhenFileMissing()
    {
        var resource = new ResourceKey("missing.bin");
        var path = Path.Combine(_tempFolder, "missing.bin");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var result = await _fileSystem.ReadAllBytesAsync(resource);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task OpenReadAsync_ReturnsStreamWithFileContent()
    {
        var resource = new ResourceKey("openread.bin");
        var path = Path.Combine(_tempFolder, "openread.bin");
        var expected = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
        await File.WriteAllBytesAsync(path, expected);
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var openResult = await _fileSystem.OpenReadAsync(resource);

        openResult.IsSuccess.Should().BeTrue();
        await using var stream = openResult.Value;
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        buffer.ToArray().Should().Equal(expected);
    }

    [Test]
    public async Task OpenWriteAsync_WritesContentThroughStream()
    {
        var resource = new ResourceKey("openwrite.bin");
        var path = Path.Combine(_tempFolder, "openwrite.bin");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var openResult = await _fileSystem.OpenWriteAsync(resource);

        openResult.IsSuccess.Should().BeTrue();
        var content = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        await using (var stream = openResult.Value)
        {
            await stream.WriteAsync(content);
        }

        File.Exists(path).Should().BeTrue();
        (await File.ReadAllBytesAsync(path)).Should().Equal(content);
    }

    [Test]
    public async Task OpenWriteAsync_CreatesParentFolder()
    {
        var resource = new ResourceKey("nested/folder/openwrite.bin");
        var path = Path.Combine(_tempFolder, "nested", "folder", "openwrite.bin");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var openResult = await _fileSystem.OpenWriteAsync(resource);

        openResult.IsSuccess.Should().BeTrue();
        await using (var stream = openResult.Value)
        {
            stream.WriteByte(0x99);
        }

        File.Exists(path).Should().BeTrue();
    }

    [Test]
    public async Task ExistsAsync_ReturnsTrue_WhenFilePresent()
    {
        var resource = new ResourceKey("present.txt");
        var path = Path.Combine(_tempFolder, "present.txt");
        await File.WriteAllTextAsync(path, "content");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var result = await _fileSystem.ExistsAsync(resource);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Test]
    public async Task ExistsAsync_ReturnsFalse_WhenFileMissing()
    {
        var resource = new ResourceKey("missing.txt");
        var path = Path.Combine(_tempFolder, "missing.txt");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var result = await _fileSystem.ExistsAsync(resource);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Test]
    public async Task ExistsAsync_ReturnsFailure_WhenResolveFails()
    {
        var resource = new ResourceKey("bad.txt");
        _resourceRegistry.ResolveResourcePath(resource)
            .Returns(Result<string>.Fail("simulated resolve failure"));

        var result = await _fileSystem.ExistsAsync(resource);

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public void MoveAsync_ThrowsNotImplementedException_InPhase1a()
    {
        // Structural operations land in fs-1b.
        Assert.ThrowsAsync<NotImplementedException>(
            () => _fileSystem.MoveAsync(new ResourceKey("a"), new ResourceKey("b")));
    }

    [Test]
    public void CopyAsync_ThrowsNotImplementedException_InPhase1a()
    {
        Assert.ThrowsAsync<NotImplementedException>(
            () => _fileSystem.CopyAsync(new ResourceKey("a"), new ResourceKey("b")));
    }

    [Test]
    public void DeleteAsync_ThrowsNotImplementedException_InPhase1a()
    {
        Assert.ThrowsAsync<NotImplementedException>(
            () => _fileSystem.DeleteAsync(new ResourceKey("a")));
    }
}
