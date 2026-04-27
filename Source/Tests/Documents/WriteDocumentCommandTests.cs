using Celbridge.Documents.Commands;
using Celbridge.Resources;
using Celbridge.Workspace;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Verifies WriteDocumentCommand writes content directly to disk and refreshes
/// the resource registry when a new file is created.
/// </summary>
[TestFixture]
public class WriteDocumentCommandTests
{
    private string _tempFolder = null!;
    private IResourceRegistry _resourceRegistry = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;

    [SetUp]
    public void Setup()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(WriteDocumentCommandTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);

        _resourceRegistry = Substitute.For<IResourceRegistry>();

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(_resourceRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempFolder))
        {
            Directory.Delete(_tempFolder, true);
        }
    }

    private WriteDocumentCommand CreateCommand()
    {
        return new WriteDocumentCommand(Substitute.For<ILogger<WriteDocumentCommand>>(), _workspaceWrapper);
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
        _resourceRegistry.Received(1).UpdateResourceRegistry();
    }

    [Test]
    public async Task ExecuteAsync_OverwritesExistingFile_WithoutRefreshingRegistry()
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
        _resourceRegistry.DidNotReceive().UpdateResourceRegistry();
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
