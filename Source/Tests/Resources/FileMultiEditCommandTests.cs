using Celbridge.Resources;
using Celbridge.Resources.Commands;
using Celbridge.Resources.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Verifies that FileMultiEditCommand applies a batch of text-match edits
/// atomically and sequentially, reporting post-batch line ranges.
/// </summary>
[TestFixture]
public class FileMultiEditCommandTests
{
    private string _tempFolder = null!;
    private IResourceRegistry _resourceRegistry = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;

    [SetUp]
    public void Setup()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(FileMultiEditCommandTests), Guid.NewGuid().ToString("N"));
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

    private FileMultiEditCommand CreateCommand()
    {
        return new FileMultiEditCommand(
            Substitute.For<ILogger<FileMultiEditCommand>>(),
            _workspaceWrapper);
    }

    [Test]
    public async Task MultiEdit_AppliesAllEditsSequentially_WhenAllSucceed()
    {
        var resource = new ResourceKey("notes/text.md");
        var path = Path.Combine(_tempFolder, "text.md");
        await File.WriteAllTextAsync(path, "alpha\nbeta\ngamma\n");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.Edits = new List<FileEditOperation>
        {
            new("alpha", "ALPHA"),
            new("gamma", "GAMMA")
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.AppliedCount.Should().Be(2);
        command.ResultValue.AffectedRanges.Should().HaveCount(2);
        command.ResultValue.AffectedRanges[0].FromLine.Should().Be(1);
        command.ResultValue.AffectedRanges[1].FromLine.Should().Be(3);
        var content = await File.ReadAllTextAsync(path);
        content.Should().Be("ALPHA\nbeta\nGAMMA\n");
    }

    [Test]
    public async Task MultiEdit_FailsAtomically_WhenAnyEditCannotMatch()
    {
        var resource = new ResourceKey("notes/text.md");
        var path = Path.Combine(_tempFolder, "text.md");
        var original = "alpha\nbeta\ngamma\n";
        await File.WriteAllTextAsync(path, original);
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.Edits = new List<FileEditOperation>
        {
            new("alpha", "ALPHA"),
            new("does-not-exist", "x")
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("Edit 1");
        var content = await File.ReadAllTextAsync(path);
        content.Should().Be(original);
    }

    [Test]
    public async Task MultiEdit_LaterEditCanAnchorAgainstEarlierEditsOutput()
    {
        // Edit 0 renames the function in its definition. Edit 1 anchors against
        // the renamed function in a call site that was changed by edit 0 too.
        var resource = new ResourceKey("notes/text.md");
        var path = Path.Combine(_tempFolder, "text.md");
        await File.WriteAllTextAsync(path, "foo()\nresult = foo()\n");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.Edits = new List<FileEditOperation>
        {
            new("foo()", "bar()", ReplaceAll: true),
            new("result = bar()", "result = bar() + 1")
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        var content = await File.ReadAllTextAsync(path);
        content.Should().Be("bar()\nresult = bar() + 1\n");
    }

    [Test]
    public async Task MultiEdit_ReportsFailingEditIndex_OnPartialFail()
    {
        var resource = new ResourceKey("notes/text.md");
        var path = Path.Combine(_tempFolder, "text.md");
        await File.WriteAllTextAsync(path, "x\nx\n");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.Edits = new List<FileEditOperation>
        {
            new("nope", "y")
        };

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("Edit 0");
        result.FirstErrorMessage.Should().Contain("not found");
    }

    [Test]
    public async Task MultiEdit_PreservesCRLF()
    {
        var resource = new ResourceKey("notes/crlf.txt");
        var path = Path.Combine(_tempFolder, "crlf.txt");
        await File.WriteAllTextAsync(path, "alpha\r\nbeta\r\ngamma\r\n");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.Edits = new List<FileEditOperation>
        {
            new("alpha", "ALPHA"),
            new("gamma", "GAMMA")
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        var content = await File.ReadAllTextAsync(path);
        content.Should().Be("ALPHA\r\nbeta\r\nGAMMA\r\n");
        content.Should().NotContain("\r\r");
    }

    [Test]
    public async Task MultiEdit_NormalisesCRLFInEdits()
    {
        var resource = new ResourceKey("notes/crlf.txt");
        var path = Path.Combine(_tempFolder, "crlf.txt");
        await File.WriteAllTextAsync(path, "alpha\r\nbeta\r\ngamma\r\n");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.Edits = new List<FileEditOperation>
        {
            new("alpha\nbeta", "ALPHA\nBETA")
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        var content = await File.ReadAllTextAsync(path);
        content.Should().Be("ALPHA\r\nBETA\r\ngamma\r\n");
    }

    [Test]
    public async Task MultiEdit_EmptyEditsArray_SucceedsAsNoOp()
    {
        var resource = new ResourceKey("notes/text.md");
        var path = Path.Combine(_tempFolder, "text.md");
        var original = "alpha\nbeta\n";
        await File.WriteAllTextAsync(path, original);
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.Edits = new List<FileEditOperation>();

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.AppliedCount.Should().Be(0);
        command.ResultValue.AffectedRanges.Should().BeEmpty();
        var content = await File.ReadAllTextAsync(path);
        content.Should().Be(original);
    }
}
