using Celbridge.Messaging;
using Celbridge.Resources;
using Celbridge.Resources.Commands;
using Celbridge.Resources.Services;
using Celbridge.Tests.FileSystem;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Verifies that MultiEditFileCommand applies a batch of text-match edits
/// atomically and sequentially, reporting per-edit summaries and post-batch
/// line ranges tagged by edit index.
/// </summary>
[TestFixture]
public class MultiEditFileCommandTests
{
    private string _tempFolder = null!;
    private IResourceRegistry _resourceRegistry = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;

    [SetUp]
    public void Setup()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(MultiEditFileCommandTests), Guid.NewGuid().ToString("N"));
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

    private MultiEditFileCommand CreateCommand()
    {
        return new MultiEditFileCommand(
            Substitute.For<ILogger<MultiEditFileCommand>>(),
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
        command.ResultValue.Edits.Should().HaveCount(2);
        command.ResultValue.Edits[0].MatchCount.Should().Be(1);
        command.ResultValue.Edits[1].MatchCount.Should().Be(1);
        command.ResultValue.AffectedRanges.Should().HaveCount(2);
        command.ResultValue.AffectedRanges[0].FromLine.Should().Be(1);
        command.ResultValue.AffectedRanges[0].EditIndex.Should().Be(0);
        command.ResultValue.AffectedRanges[1].FromLine.Should().Be(3);
        command.ResultValue.AffectedRanges[1].EditIndex.Should().Be(1);
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
        command.ResultValue.Edits.Should().BeEmpty();
        command.ResultValue.AffectedRanges.Should().BeEmpty();
        var content = await File.ReadAllTextAsync(path);
        content.Should().Be(original);
    }

    [Test]
    public async Task MultiEdit_TagsRangesByEditIndex_WhenMixedReplaceAllAndSurgical()
    {
        // Edit 0 replaces 'x' (3 occurrences) with replaceAll. Edit 1 is a
        // single surgical edit. The flat affectedLines list should carry an
        // editIndex on every range so the agent can distinguish them.
        var resource = new ResourceKey("notes/mixed.md");
        var path = Path.Combine(_tempFolder, "mixed.md");
        await File.WriteAllTextAsync(path, "x\nx\nMARKER\nx\n");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.Edits = new List<FileEditOperation>
        {
            new("x", "Y", ReplaceAll: true),
            new("MARKER", "TARGET")
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.AppliedCount.Should().Be(2);
        command.ResultValue.Edits[0].MatchCount.Should().Be(3);
        command.ResultValue.Edits[0].Truncated.Should().BeFalse();
        command.ResultValue.Edits[1].MatchCount.Should().Be(1);
        command.ResultValue.Edits[1].Truncated.Should().BeFalse();

        command.ResultValue.AffectedRanges.Should().HaveCount(4);
        var byEditIndex = command.ResultValue.AffectedRanges.GroupBy(r => r.EditIndex).ToDictionary(g => g.Key, g => g.ToList());
        byEditIndex[0].Should().HaveCount(3);
        byEditIndex[1].Should().HaveCount(1);
        byEditIndex[1][0].FromLine.Should().Be(3);
    }

    [Test]
    public async Task MultiEdit_MergesSameLineHitsWithinAnEdit_ButKeepsDifferentEditsSeparate()
    {
        // Edit 0 replaces "foo" with replaceAll on a line that has 3 "foo"
        // hits, and on a second line with 1 "foo" hit. Edit 1 changes
        // "marker" on line 3. Same-line hits within edit 0 should collapse
        // into one entry with MatchCount=3; line 2 keeps its own entry.
        // Edit 1's range stays separate even if it happened to share a line
        // with edit 0 — different edits never merge.
        var resource = new ResourceKey("notes/merge.md");
        var path = Path.Combine(_tempFolder, "merge.md");
        await File.WriteAllTextAsync(path, "foo bar foo foo\nfoo baz\nmarker\n");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.Edits = new List<FileEditOperation>
        {
            new("foo", "FOO", ReplaceAll: true),
            new("marker", "MARKER")
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.Edits[0].MatchCount.Should().Be(4);
        command.ResultValue.Edits[0].Truncated.Should().BeFalse();
        command.ResultValue.Edits[1].MatchCount.Should().Be(1);

        var edit0Ranges = command.ResultValue.AffectedRanges.Where(r => r.EditIndex == 0).ToList();
        edit0Ranges.Should().HaveCount(2);
        edit0Ranges[0].FromLine.Should().Be(1);
        edit0Ranges[0].MatchCount.Should().Be(3);
        edit0Ranges[1].FromLine.Should().Be(2);
        edit0Ranges[1].MatchCount.Should().Be(1);

        var edit1Ranges = command.ResultValue.AffectedRanges.Where(r => r.EditIndex == 1).ToList();
        edit1Ranges.Should().HaveCount(1);
        edit1Ranges[0].FromLine.Should().Be(3);
        edit1Ranges[0].MatchCount.Should().Be(1);
    }

    [Test]
    public async Task MultiEdit_CapsPerEdit_WhenOneEditExceedsThreshold()
    {
        // Edit 0 has 8 matches (> 5 threshold) — should be capped to 4 ranges
        // (first 3 + last 1) with Truncated=true. Edit 1 has 1 match — full
        // list, Truncated=false. Verifies the cap is per-edit, not batch-wide.
        var resource = new ResourceKey("notes/many.md");
        var path = Path.Combine(_tempFolder, "many.md");
        await File.WriteAllTextAsync(path, "x\nx\nx\nx\nx\nx\nx\nx\nMARKER\n");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.Edits = new List<FileEditOperation>
        {
            new("x", "Y", ReplaceAll: true),
            new("MARKER", "TARGET")
        };

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.Edits[0].MatchCount.Should().Be(8);
        command.ResultValue.Edits[0].Truncated.Should().BeTrue();
        command.ResultValue.Edits[1].MatchCount.Should().Be(1);
        command.ResultValue.Edits[1].Truncated.Should().BeFalse();

        var edit0Ranges = command.ResultValue.AffectedRanges.Where(r => r.EditIndex == 0).ToList();
        var edit1Ranges = command.ResultValue.AffectedRanges.Where(r => r.EditIndex == 1).ToList();
        edit0Ranges.Should().HaveCount(4);
        edit1Ranges.Should().HaveCount(1);
    }
}
