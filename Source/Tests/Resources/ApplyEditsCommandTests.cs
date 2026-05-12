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
    public async Task ExecuteAsync_WritesMultiFileBatch_WhenEditsTouchDifferentFiles()
    {
        var resourceOne = new ResourceKey("notes/one.md");
        var pathOne = Path.Combine(_tempFolder, "one.md");
        await File.WriteAllLinesAsync(pathOne, new[] { "first", "second" });
        _resourceRegistry.ResolveResourcePath(resourceOne).Returns(Result<string>.Ok(pathOne));

        var resourceTwo = new ResourceKey("notes/two.md");
        var pathTwo = Path.Combine(_tempFolder, "two.md");
        await File.WriteAllLinesAsync(pathTwo, new[] { "alpha", "beta" });
        _resourceRegistry.ResolveResourcePath(resourceTwo).Returns(Result<string>.Ok(pathTwo));

        var command = CreateCommand();
        command.Edits = new List<FileEdit>
        {
            new(resourceOne, new List<TextEdit> { new(1, 1, 1, -1, "FIRST") }),
            new(resourceTwo, new List<TextEdit> { new(2, 1, 2, -1, "BETA") })
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        (await File.ReadAllLinesAsync(pathOne)).Should().Equal("FIRST", "second");
        (await File.ReadAllLinesAsync(pathTwo)).Should().Equal("alpha", "BETA");
    }

    [Test]
    public async Task ExecuteAsync_PreservesCrlf_WhenNewTextUsesLfSeparators()
    {
        var resource = new ResourceKey("notes/crlf.md");
        var path = Path.Combine(_tempFolder, "crlf.md");
        await File.WriteAllTextAsync(path, "Line one\r\nLine two\r\nLine three\r\n");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        // The agent supplies NewText with \n separators. The command must
        // normalise so the file's CRLF style is preserved and no \r\r\n
        // sequences land on disk.
        var edit = new TextEdit(2, 1, 2, -1, "Two\nInserted\nThree");
        var command = CreateCommand();
        command.Edits = new List<FileEdit>
        {
            new(resource, new List<TextEdit> { edit })
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        var content = await File.ReadAllTextAsync(path);
        content.Should().Be("Line one\r\nTwo\r\nInserted\r\nThree\r\nLine three\r\n");
        content.Should().NotContain("\r\r");
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
