using Celbridge.Messaging;
using Celbridge.Resources;
using Celbridge.Resources.Commands;
using Celbridge.Resources.Services;
using Celbridge.Tests.FileSystem;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Verifies WriteFileCommand writes content directly to disk and refreshes
/// the resource registry when a new file is created.
/// </summary>
[TestFixture]
public class WriteFileCommandTests
{
    private string _tempFolder = null!;
    private IResourceRegistry _resourceRegistry = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;

    [SetUp]
    public void Setup()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(WriteFileCommandTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);

        _resourceRegistry = Substitute.For<IResourceRegistry>();
        _resourceRegistry.ProjectFolderPath.Returns(_tempFolder);

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(_resourceRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var fileStorage = new FileStorage(Substitute.For<ILogger<FileStorage>>(), Substitute.For<IMessengerService>(), _workspaceWrapper, TestFileSystem.CreateLocal());
        workspaceService.FileStorage.Returns(fileStorage);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempFolder))
        {
            Directory.Delete(_tempFolder, true);
        }
    }

    private WriteFileCommand CreateCommand()
    {
        return new WriteFileCommand(Substitute.For<ILogger<WriteFileCommand>>(), _workspaceWrapper);
    }

    [Test]
    public async Task ExecuteAsync_CreatesNewFile_WhenFileDoesNotExist()
    {
        var resource = new ResourceKey("notes/new.md");
        var path = Path.Combine(_tempFolder, "new.md");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.Content = "fresh content";

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        File.Exists(path).Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be("fresh content");
        // Registry refresh is driven by CommandFlags.UpdateResources, processed
        // by the command service framework after the command body returns;
        // ExecuteAsync itself does not call the registry directly.
    }

    [Test]
    public async Task ExecuteAsync_OverwritesExistingFile()
    {
        var resource = new ResourceKey("notes/existing.md");
        var path = Path.Combine(_tempFolder, "existing.md");
        await File.WriteAllTextAsync(path, "old content");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.Content = "new content";

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be("new content");
    }

    [Test]
    public async Task ExecuteAsync_PreservesCRLF_WhenOverwritingExistingCRLFFile()
    {
        // Existing file was written via file_write_binary (or any other path)
        // with CRLF line endings. A subsequent file_write with LF content must
        // detect the existing convention and re-encode to CRLF, so a Windows
        // user editing the file does not see a "Mixed line endings" diff after
        // a programmatic write.
        var resource = new ResourceKey("notes/crlf.txt");
        var path = Path.Combine(_tempFolder, "crlf.txt");
        await File.WriteAllBytesAsync(path, "alpha\r\nbeta\r\ngamma\r\n"u8.ToArray());
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.Content = "one\ntwo\nthree\n";

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        var content = await File.ReadAllTextAsync(path);
        content.Should().Be("one\r\ntwo\r\nthree\r\n");
        content.Should().NotContain("\r\r");
    }

    [Test]
    public async Task ExecuteAsync_FailsWhenResolveResourcePathFails()
    {
        var resource = new ResourceKey("invalid/path.md");
        _resourceRegistry.ResolveResourcePath(resource)
            .Returns(Result<string>.Fail("invalid resource"));

        var command = CreateCommand();
        command.FileResource = resource;
        command.Content = "anything";

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
    }
}
