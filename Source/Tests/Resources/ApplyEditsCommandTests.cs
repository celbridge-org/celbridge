using Celbridge.Dialog;
using Celbridge.Resources;
using Celbridge.Resources.Commands;
using Celbridge.Resources.Services;
using Celbridge.Workspace;
using Microsoft.Extensions.Localization;

namespace Celbridge.Tests.Resources;

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
        command.Edits = new List<FileEdit>
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
        command.Edits = new List<FileEdit>();

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
    }

    [Test]
    public async Task ExecuteAsync_ReportsPostEditRange_WhenEditExpandsLines()
    {
        // Replacing one line with three lines should produce a post-edit range
        // covering all three new lines.
        var resource = new ResourceKey("notes/expand.md");
        var path = Path.Combine(_tempFolder, "expand.md");
        await File.WriteAllLinesAsync(path, new[] { "First", "Two", "Last" });
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var edit = new TextEdit(2, 1, 2, -1, "Two\nInserted\nThree");
        var command = CreateCommand();
        command.Edits = new List<FileEdit>
        {
            new(resource, new List<TextEdit> { edit })
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.Should().HaveCount(1);
        command.ResultValue[0].Resource.Should().Be(resource);
        command.ResultValue[0].FromLine.Should().Be(2);
        command.ResultValue[0].ToLine.Should().Be(4);
    }

    [Test]
    public async Task ExecuteAsync_PostEditRanges_AccountForEarlierEditsInSameCall()
    {
        // Two edits in one call: an earlier one that adds 2 lines should shift
        // a later edit's post-edit range by 2.
        var resource = new ResourceKey("notes/multi.md");
        var path = Path.Combine(_tempFolder, "multi.md");
        await File.WriteAllLinesAsync(path, new[] { "A", "B", "C", "D", "E" });
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        // Replace line 2 with 3 lines (B → B1\nB2\nB3) and replace line 4 with 1 line (D → DD).
        var edit1 = new TextEdit(2, 1, 2, -1, "B1\nB2\nB3");
        var edit2 = new TextEdit(4, 1, 4, -1, "DD");
        var command = CreateCommand();
        command.Edits = new List<FileEdit>
        {
            new(resource, new List<TextEdit> { edit1, edit2 })
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.Should().HaveCount(2);
        // First edit: line 2 → lines 2-4 (3 new lines)
        command.ResultValue[0].FromLine.Should().Be(2);
        command.ResultValue[0].ToLine.Should().Be(4);
        // Second edit: original line 4, shifted by +2 from the first edit's expansion → line 6.
        command.ResultValue[1].FromLine.Should().Be(6);
        command.ResultValue[1].ToLine.Should().Be(6);
    }

    [Test]
    public async Task ExecuteAsync_FailsWhenFileMissing()
    {
        var resource = new ResourceKey("missing.md");
        var missingPath = Path.Combine(_tempFolder, "missing.md");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(missingPath));

        var command = CreateCommand();
        command.Edits = new List<FileEdit>
        {
            new(resource, new List<TextEdit> { new(1, 1, 1, -1, "x") })
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
    }
}
