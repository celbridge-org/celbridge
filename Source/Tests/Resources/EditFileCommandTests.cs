using Celbridge.Resources;
using Celbridge.Resources.Commands;
using Celbridge.Resources.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Verifies that EditFileCommand replaces text by exact-snippet match,
/// preserves the file's line-ending convention, and fails closed when the
/// snippet is missing or non-unique.
/// </summary>
[TestFixture]
public class EditFileCommandTests
{
    private string _tempFolder = null!;
    private IResourceRegistry _resourceRegistry = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;

    [SetUp]
    public void Setup()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(EditFileCommandTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);

        _resourceRegistry = Substitute.For<IResourceRegistry>();
        _resourceRegistry.ProjectFolderPath.Returns(_tempFolder);

        var resourceService = Substitute.For<IResourceService>();
        resourceService.Registry.Returns(_resourceRegistry);

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var fileSystem = new ResourceFileSystem(Substitute.For<ILogger<ResourceFileSystem>>(), _workspaceWrapper);
        workspaceService.ResourceFileSystem.Returns(fileSystem);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempFolder))
        {
            Directory.Delete(_tempFolder, true);
        }
    }

    private EditFileCommand CreateCommand()
    {
        return new EditFileCommand(
            Substitute.For<ILogger<EditFileCommand>>(),
            _workspaceWrapper);
    }

    [Test]
    public async Task Edit_ReplacesUniqueMatch_WhenSingleOccurrence()
    {
        var resource = new ResourceKey("notes/text.md");
        var path = Path.Combine(_tempFolder, "text.md");
        await File.WriteAllTextAsync(path, "alpha\nbeta\ngamma\n");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.OldString = "beta";
        command.NewString = "BETA";

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.MatchCount.Should().Be(1);
        command.ResultValue.AffectedRanges.Should().HaveCount(1);
        command.ResultValue.AffectedRanges[0].FromLine.Should().Be(2);
        command.ResultValue.AffectedRanges[0].ToLine.Should().Be(2);
        var content = await File.ReadAllTextAsync(path);
        content.Should().Contain("BETA");
        content.Should().NotContain("beta");
    }

    [Test]
    public async Task Edit_FailsWithDisambiguationHint_WhenMultipleMatchesAndReplaceAllFalse()
    {
        var resource = new ResourceKey("notes/text.md");
        var path = Path.Combine(_tempFolder, "text.md");
        await File.WriteAllTextAsync(path, "x\nx\nx\n");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.OldString = "x";
        command.NewString = "y";

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("3 occurrences");
        result.FirstErrorMessage.Should().Contain("replaceAll");
        var content = await File.ReadAllTextAsync(path);
        content.Should().Be("x\nx\nx\n");
    }

    [Test]
    public async Task Edit_ReplacesAllOccurrences_WhenReplaceAllTrue()
    {
        var resource = new ResourceKey("notes/text.md");
        var path = Path.Combine(_tempFolder, "text.md");
        await File.WriteAllTextAsync(path, "x\nx\nx\n");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.OldString = "x";
        command.NewString = "y";
        command.ReplaceAll = true;

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.MatchCount.Should().Be(3);
        command.ResultValue.AffectedRanges.Should().HaveCount(3);
        var ranges = command.ResultValue.AffectedRanges;
        ranges[0].FromLine.Should().Be(1);
        ranges[1].FromLine.Should().Be(2);
        ranges[2].FromLine.Should().Be(3);
        var content = await File.ReadAllTextAsync(path);
        content.Should().Be("y\ny\ny\n");
    }

    [Test]
    public async Task Edit_FailsWithQuoteHint_WhenOldStringNotFound()
    {
        var resource = new ResourceKey("notes/text.md");
        var path = Path.Combine(_tempFolder, "text.md");
        await File.WriteAllTextAsync(path, "alpha\nbeta\n");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.OldString = "missing snippet";
        command.NewString = "x";

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("not found");
        result.FirstErrorMessage.Should().Contain("missing snippet");
    }

    [Test]
    public async Task Edit_PreservesCRLF_WhenFileHasCRLF()
    {
        var resource = new ResourceKey("notes/crlf.txt");
        var path = Path.Combine(_tempFolder, "crlf.txt");
        await File.WriteAllTextAsync(path, "alpha\r\nbeta\r\ngamma\r\n");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.OldString = "beta";
        command.NewString = "BETA";

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        var content = await File.ReadAllTextAsync(path);
        content.Should().Be("alpha\r\nBETA\r\ngamma\r\n");
        content.Should().NotContain("\r\r");
    }

    [Test]
    public async Task Edit_NormalisesCRLFInOldString_WhenAgentPassesCRLF()
    {
        var resource = new ResourceKey("notes/lf.txt");
        var path = Path.Combine(_tempFolder, "lf.txt");
        await File.WriteAllTextAsync(path, "alpha\nbeta\ngamma\n");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.OldString = "alpha\r\nbeta";
        command.NewString = "ALPHA\nBETA";

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        var content = await File.ReadAllTextAsync(path);
        content.Should().Be("ALPHA\nBETA\ngamma\n");
    }

    [Test]
    public async Task Edit_NormalisesLFInNewString_WhenFileIsCRLF()
    {
        var resource = new ResourceKey("notes/crlf.txt");
        var path = Path.Combine(_tempFolder, "crlf.txt");
        await File.WriteAllTextAsync(path, "alpha\r\nbeta\r\ngamma\r\n");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.OldString = "beta";
        command.NewString = "BETA\nNEXT";

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        var content = await File.ReadAllTextAsync(path);
        content.Should().Be("alpha\r\nBETA\r\nNEXT\r\ngamma\r\n");
    }

    [Test]
    public async Task Edit_FailsForEmptyOldString_WithAppendAndCreateHints()
    {
        var command = CreateCommand();
        command.FileResource = new ResourceKey("any.md");
        command.OldString = string.Empty;
        command.NewString = "anything";

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("append");
        result.FirstErrorMessage.Should().Contain("anchor on the existing last line");
        result.FirstErrorMessage.Should().Contain("file_write");
    }

    [Test]
    public async Task Edit_CapsAffectedRanges_AndSetsTruncated_WhenMatchCountExceedsThreshold()
    {
        // 8 matches > VerboseRangeThreshold (5). Expect the first 3 plus the
        // last 1 to come back, with Truncated=true and MatchCount=8 unchanged.
        var resource = new ResourceKey("notes/many.md");
        var path = Path.Combine(_tempFolder, "many.md");
        await File.WriteAllTextAsync(path, "x\nx\nx\nx\nx\nx\nx\nx\n");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.OldString = "x";
        command.NewString = "Y";
        command.ReplaceAll = true;

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.MatchCount.Should().Be(8);
        command.ResultValue.Truncated.Should().BeTrue();
        command.ResultValue.AffectedRanges.Should().HaveCount(4);
        command.ResultValue.AffectedRanges[0].FromLine.Should().Be(1);
        command.ResultValue.AffectedRanges[1].FromLine.Should().Be(2);
        command.ResultValue.AffectedRanges[2].FromLine.Should().Be(3);
        command.ResultValue.AffectedRanges[3].FromLine.Should().Be(8);
    }

    [Test]
    public async Task Edit_KeepsFullRangeList_WhenMatchCountAtOrBelowThreshold()
    {
        // 5 matches == threshold. Expect full list, Truncated=false.
        var resource = new ResourceKey("notes/five.md");
        var path = Path.Combine(_tempFolder, "five.md");
        await File.WriteAllTextAsync(path, "x\nx\nx\nx\nx\n");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.OldString = "x";
        command.NewString = "Y";
        command.ReplaceAll = true;

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.MatchCount.Should().Be(5);
        command.ResultValue.Truncated.Should().BeFalse();
        command.ResultValue.AffectedRanges.Should().HaveCount(5);
    }

    [Test]
    public async Task Edit_FailsForMissingFile()
    {
        var resource = new ResourceKey("missing.md");
        var missingPath = Path.Combine(_tempFolder, "missing.md");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(missingPath));

        var command = CreateCommand();
        command.FileResource = resource;
        command.OldString = "x";
        command.NewString = "y";

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task Edit_AppendUseCase_WorksByMatchingFinalLine()
    {
        // Canonical append workflow: anchor against a suffix of the existing
        // file and concatenate the appended text in newString. No coordinates
        // needed.
        var resource = new ResourceKey("notes/append.md");
        var path = Path.Combine(_tempFolder, "append.md");
        await File.WriteAllTextAsync(path, "first line\nlast line\n");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.OldString = "last line\n";
        command.NewString = "last line\nappended one\nappended two\n";

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        var content = await File.ReadAllTextAsync(path);
        content.Should().Be("first line\nlast line\nappended one\nappended two\n");
    }

    [Test]
    public async Task Edit_ReportsToLine_WithoutCountingTrailingNewlineInNewString()
    {
        // Regression: a trailing newline in newString terminates the last
        // content line; it must not bump toLine by one.
        var resource = new ResourceKey("notes/range.md");
        var path = Path.Combine(_tempFolder, "range.md");
        await File.WriteAllTextAsync(path, "alpha\nbeta\ngamma\ndelta\nepsilon\n");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.OldString = "beta\ngamma\ndelta\n";
        command.NewString = "B\nC\nD\n";

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.AffectedRanges.Should().HaveCount(1);
        command.ResultValue.AffectedRanges[0].FromLine.Should().Be(2);
        command.ResultValue.AffectedRanges[0].ToLine.Should().Be(4);
    }

    [Test]
    public async Task Edit_AppendByLastLineAnchor_ReportsRangeOverAppendedLines()
    {
        // Append two lines to a 5-line file by anchoring on the last line.
        // The reported toLine must point at the last appended content line,
        // not past EOF.
        var resource = new ResourceKey("notes/append.md");
        var path = Path.Combine(_tempFolder, "append.md");
        await File.WriteAllTextAsync(path, "one\ntwo\nthree\nfour\nfive\n");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.OldString = "five\n";
        command.NewString = "five\nsix\nseven\n";

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.AffectedRanges.Should().HaveCount(1);
        command.ResultValue.AffectedRanges[0].FromLine.Should().Be(5);
        command.ResultValue.AffectedRanges[0].ToLine.Should().Be(7);
        var content = await File.ReadAllTextAsync(path);
        content.Should().Be("one\ntwo\nthree\nfour\nfive\nsix\nseven\n");
    }

    [Test]
    public async Task Edit_MergesSameLineHits_WhenReplaceAllProducesMultipleMatchesOnSameLine()
    {
        // Two matches of "foo" on line 1, one on line 2. Same-(from,to) hits
        // on line 1 collapse into a single entry with MatchCount=2; line 2
        // keeps its own entry with MatchCount=1. Top-level MatchCount stays
        // at the raw total of 3.
        var resource = new ResourceKey("notes/dups.md");
        var path = Path.Combine(_tempFolder, "dups.md");
        await File.WriteAllTextAsync(path, "foo bar foo\nfoo baz\n");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.OldString = "foo";
        command.NewString = "FOO";
        command.ReplaceAll = true;

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.MatchCount.Should().Be(3);
        command.ResultValue.AffectedRanges.Should().HaveCount(2);
        command.ResultValue.AffectedRanges[0].FromLine.Should().Be(1);
        command.ResultValue.AffectedRanges[0].ToLine.Should().Be(1);
        command.ResultValue.AffectedRanges[0].MatchCount.Should().Be(2);
        command.ResultValue.AffectedRanges[1].FromLine.Should().Be(2);
        command.ResultValue.AffectedRanges[1].MatchCount.Should().Be(1);
    }

    [Test]
    public async Task Edit_DeletesMultiLineBlock_WhenOldStringIncludesTrailingNewline()
    {
        // Canonical multi-line deletion: include the trailing line terminator
        // so the empty line is removed too.
        var resource = new ResourceKey("notes/delete.md");
        var path = Path.Combine(_tempFolder, "delete.md");
        await File.WriteAllTextAsync(path, "alpha\nbeta\ngamma\ndelta\n");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.OldString = "beta\ngamma\n";
        command.NewString = string.Empty;

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        var content = await File.ReadAllTextAsync(path);
        content.Should().Be("alpha\ndelta\n");
    }
}
