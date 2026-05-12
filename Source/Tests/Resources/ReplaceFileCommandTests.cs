using Celbridge.Resources;
using Celbridge.Resources.Commands;
using Celbridge.Resources.Services;
using Celbridge.Workspace;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Verifies that ReplaceFileCommand applies replacements directly to
/// the file on disk and reports the replacement count, affected ranges, and
/// truncation flag.
/// </summary>
[TestFixture]
public class ReplaceFileCommandTests
{
    private string _tempFolder = null!;
    private IResourceRegistry _resourceRegistry = null!;
    private IWorkspaceWrapper _workspaceWrapper = null!;

    [SetUp]
    public void Setup()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(ReplaceFileCommandTests), Guid.NewGuid().ToString("N"));
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

    private ReplaceFileCommand CreateCommand()
    {
        return new ReplaceFileCommand(
            Substitute.For<ILogger<ReplaceFileCommand>>(),
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
        command.ResultValue.ReplacementCount.Should().Be(2);
        command.ResultValue.AffectedRanges.Should().HaveCount(2);
        command.ResultValue.AffectedRanges[0].FromLine.Should().Be(1);
        command.ResultValue.AffectedRanges[1].FromLine.Should().Be(2);
        command.ResultValue.Truncated.Should().BeFalse();
        var content = await File.ReadAllTextAsync(path);
        content.Should().Contain("the lazy brown fox");
        content.Should().Contain("the lazy brown dog");
    }

    [Test]
    public async Task ExecuteAsync_ReturnsEmptyRanges_WhenNoMatches()
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
        command.ResultValue.ReplacementCount.Should().Be(0);
        command.ResultValue.AffectedRanges.Should().BeEmpty();
        command.ResultValue.Truncated.Should().BeFalse();
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

    [Test]
    public async Task ExecuteAsync_ReturnsEmptyRanges_WhenRegexHasNoMatches()
    {
        var resource = new ResourceKey("notes/regex.md");
        var path = Path.Combine(_tempFolder, "regex.md");
        var original = "alpha\nbeta\ngamma\n";
        await File.WriteAllTextAsync(path, original);
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.SearchText = @"\d+";
        command.ReplaceText = "X";
        command.UseRegex = true;

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.ReplacementCount.Should().Be(0);
        command.ResultValue.AffectedRanges.Should().BeEmpty();
        command.ResultValue.Truncated.Should().BeFalse();
        // The file is not rewritten when the regex matches nothing.
        var content = await File.ReadAllTextAsync(path);
        content.Should().Be(original);
    }

    [Test]
    public async Task ExecuteAsync_TracksRangesForRegexBackReferences()
    {
        var resource = new ResourceKey("notes/regex.md");
        var path = Path.Combine(_tempFolder, "regex.md");
        await File.WriteAllTextAsync(path, "alpha_old\nbeta_old\ngamma_old\n");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.SearchText = @"(\w+)_old";
        command.ReplaceText = "$1_new";
        command.UseRegex = true;

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.ReplacementCount.Should().Be(3);
        command.ResultValue.AffectedRanges.Should().HaveCount(3);
        command.ResultValue.AffectedRanges[0].FromLine.Should().Be(1);
        command.ResultValue.AffectedRanges[2].FromLine.Should().Be(3);
        var content = await File.ReadAllTextAsync(path);
        content.Should().Be("alpha_new\nbeta_new\ngamma_new\n");
    }

    [Test]
    public async Task ExecuteAsync_CapsRanges_WhenReplacementCountExceedsThreshold()
    {
        var resource = new ResourceKey("notes/many.md");
        var path = Path.Combine(_tempFolder, "many.md");
        await File.WriteAllTextAsync(path, "x\nx\nx\nx\nx\nx\nx\nx\n");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.SearchText = "x";
        command.ReplaceText = "Y";

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.ReplacementCount.Should().Be(8);
        command.ResultValue.Truncated.Should().BeTrue();
        command.ResultValue.AffectedRanges.Should().HaveCount(4);
        command.ResultValue.AffectedRanges[0].FromLine.Should().Be(1);
        command.ResultValue.AffectedRanges[3].FromLine.Should().Be(8);
    }

    [Test]
    public async Task ExecuteAsync_MatchWord_DoesNotMatchSubstrings()
    {
        // The classic substring footgun: replacing 'foo' inside 'food'.
        // matchWord constrains the match to word boundaries, so 'food' and
        // 'myfoo' are left alone while standalone 'foo' is replaced.
        var resource = new ResourceKey("notes/word.md");
        var path = Path.Combine(_tempFolder, "word.md");
        await File.WriteAllTextAsync(path, "foo bar\nfood truck\nmyfoo\nfoo end\n");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.SearchText = "foo";
        command.ReplaceText = "BAR";
        command.MatchWord = true;
        command.MatchCase = true;

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.ReplacementCount.Should().Be(2);
        var content = await File.ReadAllTextAsync(path);
        content.Should().Be("BAR bar\nfood truck\nmyfoo\nBAR end\n");
    }

    [Test]
    public async Task ExecuteAsync_MatchWord_EscapesRegexMetacharactersInLiteralSearch()
    {
        // matchWord wraps the literal in \b...\b. The literal must be regex-
        // escaped so metacharacters like '.' or '(' don't get reinterpreted.
        var resource = new ResourceKey("notes/dotted.md");
        var path = Path.Combine(_tempFolder, "dotted.md");
        await File.WriteAllTextAsync(path, "a.b\nax.b\na_b\n");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.SearchText = "a.b";
        command.ReplaceText = "XYZ";
        command.MatchWord = true;
        command.MatchCase = true;

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        // matchWord requires word boundaries on both ends of the literal "a.b".
        // 'a' and 'b' are word characters; '.' is not. \ba.b\b matches "a.b"
        // (boundary before 'a', after 'b'). Critically, the regex-escaped '.'
        // does NOT match 'x', so "ax.b" is left alone.
        var content = await File.ReadAllTextAsync(path);
        content.Should().Be("XYZ\nax.b\na_b\n");
    }

    [Test]
    public async Task ExecuteAsync_MatchWord_IgnoredWhenUseRegexIsTrue()
    {
        // When useRegex is true, matchWord is ignored — regex callers add
        // their own \b anchors. With useRegex and no \b, 'foo' inside 'food'
        // matches as a substring just like without matchWord.
        var resource = new ResourceKey("notes/regex.md");
        var path = Path.Combine(_tempFolder, "regex.md");
        await File.WriteAllTextAsync(path, "foo\nfood\n");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.SearchText = "foo";
        command.ReplaceText = "BAR";
        command.UseRegex = true;
        command.MatchWord = true; // ignored
        command.MatchCase = true;

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.ReplacementCount.Should().Be(2);
        var content = await File.ReadAllTextAsync(path);
        content.Should().Be("BAR\nBARd\n");
    }

    [Test]
    public async Task ExecuteAsync_MergesSameLineHits_WhenLineContainsMultipleMatches()
    {
        // Three matches of "the" on line 1, one on line 2. Same-line hits on
        // line 1 collapse into a single entry with MatchCount=3; line 2 keeps
        // its own entry. Top-level ReplacementCount stays at the raw total 4.
        var resource = new ResourceKey("notes/the.md");
        var path = Path.Combine(_tempFolder, "the.md");
        await File.WriteAllTextAsync(path, "the dog the cat the fox\nthe end\n");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.SearchText = "the";
        command.ReplaceText = "THE";
        command.MatchCase = true;

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.ReplacementCount.Should().Be(4);
        command.ResultValue.AffectedRanges.Should().HaveCount(2);
        command.ResultValue.AffectedRanges[0].FromLine.Should().Be(1);
        command.ResultValue.AffectedRanges[0].MatchCount.Should().Be(3);
        command.ResultValue.AffectedRanges[1].FromLine.Should().Be(2);
        command.ResultValue.AffectedRanges[1].MatchCount.Should().Be(1);
    }

    [Test]
    public async Task ExecuteAsync_TracksRangesWithinLineRangeScope()
    {
        var resource = new ResourceKey("notes/scoped.md");
        var path = Path.Combine(_tempFolder, "scoped.md");
        await File.WriteAllTextAsync(path, "x\nx\nx\nx\nx\n");
        _resourceRegistry.ResolveResourcePath(resource).Returns(Result<string>.Ok(path));

        var command = CreateCommand();
        command.FileResource = resource;
        command.SearchText = "x";
        command.ReplaceText = "Y";
        command.FromLine = 2;
        command.ToLine = 4;

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.ReplacementCount.Should().Be(3);
        command.ResultValue.AffectedRanges.Should().HaveCount(3);
        command.ResultValue.AffectedRanges[0].FromLine.Should().Be(2);
        command.ResultValue.AffectedRanges[2].FromLine.Should().Be(4);
        var content = await File.ReadAllTextAsync(path);
        content.Should().Be("x\nY\nY\nY\nx\n");
    }
}
