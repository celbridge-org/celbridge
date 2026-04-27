using Celbridge.Dialog;
using Celbridge.Documents.Commands;
using Celbridge.Resources;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Verifies that ApplyEditsCommand writes edits directly to disk and never
/// routes through any editor channel.
/// </summary>
[TestFixture]
public class ApplyEditsCommandTests
{
    private string _tempFolder = null!;
    private IResourceRegistry _resourceRegistry = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;

    [SetUp]
    public void Setup()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(ApplyEditsCommandTests), Guid.NewGuid().ToString("N"));
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

    private ApplyEditsCommand CreateCommand()
    {
        return new ApplyEditsCommand(
            Substitute.For<ILogger<ApplyEditsCommand>>(),
            Substitute.For<IStringLocalizer>(),
            Substitute.For<IDialogService>(),
            _workspaceWrapper);
    }

    [Test]
    public async Task ExecuteAsync_WritesEditsToDisk_WhenFileExists()
    {
        var resource = new ResourceKey("notes/file.md");
        var path = Path.Combine(_tempFolder, "file.md");
        await File.WriteAllLinesAsync(path, new[] { "Line one", "Line two", "Line three" });
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var edit = new TextEdit(2, 1, 2, -1, "Replaced");
        var command = CreateCommand();
        command.Edits = new List<DocumentEdit>
        {
            new(resource, new List<TextEdit> { edit })
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        var lines = await File.ReadAllLinesAsync(path);
        lines.Should().Equal("Line one", "Replaced", "Line three");
    }

    [Test]
    public async Task ExecuteAsync_ReturnsOk_WhenNoEdits()
    {
        var command = CreateCommand();
        command.Edits = new List<DocumentEdit>();

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
    }

    [Test]
    public async Task ExecuteAsync_FailsWhenFileMissing()
    {
        var resource = new ResourceKey("missing.md");
        var missingPath = Path.Combine(_tempFolder, "missing.md");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(missingPath));

        var command = CreateCommand();
        command.Edits = new List<DocumentEdit>
        {
            new(resource, new List<TextEdit> { new(1, 1, 1, -1, "x") })
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
    }
}
