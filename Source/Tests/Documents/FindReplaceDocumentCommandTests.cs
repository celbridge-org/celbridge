using Celbridge.Documents.Commands;
using Celbridge.Resources;
using Celbridge.Workspace;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Verifies that FindReplaceDocumentCommand applies replacements directly to
/// the file on disk and reports the replacement count.
/// </summary>
[TestFixture]
public class FindReplaceDocumentCommandTests
{
    private string _tempFolder = null!;
    private IResourceRegistry _resourceRegistry = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;

    [SetUp]
    public void Setup()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(FindReplaceDocumentCommandTests), Guid.NewGuid().ToString("N"));
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

    private FindReplaceDocumentCommand CreateCommand()
    {
        return new FindReplaceDocumentCommand(
            Substitute.For<ILogger<FindReplaceDocumentCommand>>(),
            _workspaceWrapper);
    }

    [Test]
    public async Task ExecuteAsync_ReplacesPlainTextOnDisk()
    {
        var resource = new ResourceKey("notes/text.md");
        var path = Path.Combine(_tempFolder, "text.md");
        await File.WriteAllTextAsync(path, "the quick brown fox\nthe quick brown dog\n");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.SearchText = "quick";
        command.ReplaceText = "lazy";

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.Should().Be(2);
        var content = await File.ReadAllTextAsync(path);
        content.Should().Contain("the lazy brown fox");
        content.Should().Contain("the lazy brown dog");
    }

    [Test]
    public async Task ExecuteAsync_ReturnsZeroCount_WhenNoMatches()
    {
        var resource = new ResourceKey("notes/text.md");
        var path = Path.Combine(_tempFolder, "text.md");
        await File.WriteAllTextAsync(path, "the quick brown fox\n");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.SearchText = "nothing";
        command.ReplaceText = "x";

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.Should().Be(0);
    }

    [Test]
    public async Task ExecuteAsync_FailsWhenSearchTextEmpty()
    {
        var command = CreateCommand();
        command.FileResource = new ResourceKey("any.md");
        command.SearchText = string.Empty;

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
        command.FileResource = resource;
        command.SearchText = "x";

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
    }
}
