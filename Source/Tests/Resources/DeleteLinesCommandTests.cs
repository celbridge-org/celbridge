using Celbridge.Resources;
using Celbridge.Resources.Commands;
using Celbridge.Resources.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Verifies that DeleteLinesCommand removes the requested line range from disk.
/// </summary>
[TestFixture]
public class DeleteLinesCommandTests
{
    private string _tempFolder = null!;
    private IResourceRegistry _resourceRegistry = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;

    [SetUp]
    public void Setup()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(DeleteLinesCommandTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);

        _resourceRegistry = Substitute.For<IResourceRegistry>();
        _resourceRegistry.ProjectFolderPath.Returns(_tempFolder);

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(_resourceRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var fileWriter = new ResourceFileWriter(Substitute.For<ILogger<ResourceFileWriter>>(), _workspaceWrapper);
        resourceService.FileWriter.Returns(fileWriter);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempFolder))
        {
            Directory.Delete(_tempFolder, true);
        }
    }

    private DeleteLinesCommand CreateCommand()
    {
        return new DeleteLinesCommand(
            Substitute.For<ILogger<DeleteLinesCommand>>(),
            _workspaceWrapper);
    }

    [Test]
    public async Task ExecuteAsync_DeletesRangeOnDisk()
    {
        var resource = new ResourceKey("notes/file.md");
        var path = Path.Combine(_tempFolder, "file.md");
        await File.WriteAllLinesAsync(path, new[] { "Line one", "Line two", "Line three", "Line four" });
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.Resource = resource;
        command.StartLine = 2;
        command.EndLine = 3;

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        var lines = await File.ReadAllLinesAsync(path);
        lines.Should().Equal("Line one", "Line four");
    }

    [Test]
    public async Task ExecuteAsync_FailsWhenStartLineLessThanOne()
    {
        var command = CreateCommand();
        command.Resource = new ResourceKey("any.md");
        command.StartLine = 0;
        command.EndLine = 1;

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task ExecuteAsync_FailsWhenEndLineBeforeStart()
    {
        var command = CreateCommand();
        command.Resource = new ResourceKey("any.md");
        command.StartLine = 5;
        command.EndLine = 3;

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task ExecuteAsync_FailsWhenFileMissing()
    {
        var resource = new ResourceKey("missing.md");
        var missingPath = Path.Combine(_tempFolder, "missing.md");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(missingPath));

        var command = CreateCommand();
        command.Resource = resource;
        command.StartLine = 1;
        command.EndLine = 1;

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
    }
}
