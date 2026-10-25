using Celbridge.Messaging;
using Celbridge.Resources;
using Celbridge.Resources.Commands;
using Celbridge.Resources.Services;
using Celbridge.Tests.FileSystem;
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
    private IWorkspaceWrapper _workspaceWrapper = null!;

    [SetUp]
    public void Setup()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), "Celbridge", nameof(ReplaceFileCommandTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);

        var resourceService = Substitute.For<IResourceService>();
        resourceService.RootHandlers.Returns(TestRootHandlers.Create(_tempFolder));

        var workspaceService = Substitute.For<IWorkspaceService>();
        workspaceService.ResourceService.Returns(resourceService);
        resourceService.Policy.Returns(TestResourcePolicy.CreateDefault());

        _workspaceWrapper = Substitute.For<IWorkspaceWrapper>();
        _workspaceWrapper.WorkspaceService.Returns(workspaceService);

        var resourceFileSystem = new LocalResourceFileSystem(Substitute.For<ILogger<LocalResourceFileSystem>>(), Substitute.For<IMessengerService>(), _workspaceWrapper, TestFileSystem.CreateLocal());
        resourceService.FileSystem.Returns(resourceFileSystem);
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
        var resource = new ResourceKey("text.md");
        var path = Path.Combine(_tempFolder, "text.md");
        await File.WriteAllTextAsync(path, "the quick brown fox\nthe quick brown dog\n");

        var command = CreateCommand();
        command.FileResource = resource;
        command.Pattern = "quick";
        command.Replacement = "lazy";

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
        var resource = new ResourceKey("text.md");
        var path = Path.Combine(_tempFolder, "text.md");
        await File.WriteAllTextAsync(path, "the quick brown fox\n");

        var command = CreateCommand();
        command.FileResource = resource;
        command.Pattern = "nothing";
        command.Replacement = "x";

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.ReplacementCount.Should().Be(0);
        command.ResultValue.AffectedRanges.Should().BeEmpty();
        command.ResultValue.Truncated.Should().BeFalse();
    }

    [Test]
    public async Task ExecuteAsync_FailsWhenPatternEmpty()
    {
        var command = CreateCommand();
        command.FileResource = new ResourceKey("any.md");
        command.Pattern = string.Empty;

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task ExecuteAsync_FailsWhenFileMissing()
    {
        var resource = new ResourceKey("missing.md");

        var command = CreateCommand();
        command.FileResource = resource;
        command.Pattern = "x";

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task ExecuteAsync_ReturnsEmptyRanges_WhenRegexHasNoMatches()
    {
        var resource = new ResourceKey("regex.md");
        var path = Path.Combine(_tempFolder, "regex.md");
        var original = "alpha\nbeta\ngamma\n";
        await File.WriteAllTextAsync(path, original);

        var command = CreateCommand();
        command.FileResource = resource;
        command.Pattern = @"\d+";
        command.Replacement = "X";
        command.Literal = false;

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
    public async Task ExecuteAsync_TracksRangesForRegexGroupReferences()
    {
        var resource = new ResourceKey("regex.md");
        var path = Path.Combine(_tempFolder, "regex.md");
        await File.WriteAllTextAsync(path, "alpha_old\nbeta_old\ngamma_old\n");

        var command = CreateCommand();
        command.FileResource = resource;
        command.Pattern = @"(\w+)_old";
        command.Replacement = "${1}_new";
        command.Literal = false;

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
        var resource = new ResourceKey("many.md");
        var path = Path.Combine(_tempFolder, "many.md");
        await File.WriteAllTextAsync(path, "x\nx\nx\nx\nx\nx\nx\nx\n");

        var command = CreateCommand();
        command.FileResource = resource;
        command.Pattern = "x";
        command.Replacement = "Y";

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.ReplacementCount.Should().Be(8);
        command.ResultValue.Truncated.Should().BeTrue();
        command.ResultValue.AffectedRanges.Should().HaveCount(4);
        command.ResultValue.AffectedRanges[0].FromLine.Should().Be(1);
        command.ResultValue.AffectedRanges[3].FromLine.Should().Be(8);
    }

    [Test]
    public async Task ExecuteAsync_WholeWord_DoesNotMatchSubstrings()
    {
        // The classic substring footgun: replacing 'foo' inside 'food'.
        // wholeWord constrains the match to whole words, so 'food' and
        // 'myfoo' are left alone while standalone 'foo' is replaced.
        var resource = new ResourceKey("word.md");
        var path = Path.Combine(_tempFolder, "word.md");
        await File.WriteAllTextAsync(path, "foo bar\nfood truck\nmyfoo\nfoo end\n");

        var command = CreateCommand();
        command.FileResource = resource;
        command.Pattern = "foo";
        command.Replacement = "BAR";
        command.WholeWord = true;
        command.MatchCase = true;

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.ReplacementCount.Should().Be(2);
        var content = await File.ReadAllTextAsync(path);
        content.Should().Be("BAR bar\nfood truck\nmyfoo\nBAR end\n");
    }

    [Test]
    public async Task ExecuteAsync_WholeWord_ReadsALiteralAsPlainText()
    {
        // The '.' in a literal pattern is plain text, so "ax.b" is left alone. "a_b" contains no "a.b" at all.
        var resource = new ResourceKey("dotted.md");
        var path = Path.Combine(_tempFolder, "dotted.md");
        await File.WriteAllTextAsync(path, "a.b\nax.b\na_b\n");

        var command = CreateCommand();
        command.FileResource = resource;
        command.Pattern = "a.b";
        command.Replacement = "XYZ";
        command.WholeWord = true;
        command.MatchCase = true;

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        var content = await File.ReadAllTextAsync(path);
        content.Should().Be("XYZ\nax.b\na_b\n");
    }

    [Test]
    public async Task ExecuteAsync_WholeWord_AppliesToARegex()
    {
        var resource = new ResourceKey("regex.md");
        var path = Path.Combine(_tempFolder, "regex.md");
        await File.WriteAllTextAsync(path, "foo\nfood\n");

        var command = CreateCommand();
        command.FileResource = resource;
        command.Pattern = "fo+";
        command.Replacement = "BAR";
        command.Literal = false;
        command.WholeWord = true;

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.ReplacementCount.Should().Be(1);
        var content = await File.ReadAllTextAsync(path);
        content.Should().Be("BAR\nfood\n");
    }

    [Test]
    public async Task ExecuteAsync_ReadsThePatternAsPlainTextByDefault()
    {
        var resource = new ResourceKey("plain.md");
        var path = Path.Combine(_tempFolder, "plain.md");
        await File.WriteAllTextAsync(path, "price $5 (each)\nprice 5 each\n");

        var command = CreateCommand();
        command.FileResource = resource;
        command.Pattern = "$5 (each)";
        command.Replacement = "$6 (each)";

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.ReplacementCount.Should().Be(1);
        var content = await File.ReadAllTextAsync(path);
        content.Should().Be("price $6 (each)\nprice 5 each\n");
    }

    [Test]
    public async Task ExecuteAsync_RegexWithoutMultiline_MatchesWithinOneLine()
    {
        var resource = new ResourceKey("lines.md");
        var path = Path.Combine(_tempFolder, "lines.md");
        await File.WriteAllTextAsync(path, "alpha\nbeta\n");

        var command = CreateCommand();
        command.FileResource = resource;
        command.Pattern = "a.*";
        command.Replacement = "X";
        command.Literal = false;

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.ReplacementCount.Should().Be(2);
        var content = await File.ReadAllTextAsync(path);
        content.Should().Be("X\nbetX\n");
    }

    [Test]
    public async Task ExecuteAsync_Multiline_MatchesAcrossLinesAndKeepsTheFilesLineEndings()
    {
        var resource = new ResourceKey("crlf.md");
        var path = Path.Combine(_tempFolder, "crlf.md");
        await File.WriteAllTextAsync(path, "import a\r\nfrom b\r\nrest\r\n");

        var command = CreateCommand();
        command.FileResource = resource;
        command.Pattern = @"import (\w+)\nfrom (\w+)";
        command.Replacement = "from $2\nimport $1";
        command.Literal = false;
        command.Multiline = true;

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.ReplacementCount.Should().Be(1);
        var content = await File.ReadAllTextAsync(path);
        content.Should().Be("from b\r\nimport a\r\nrest\r\n");
    }

    [Test]
    public async Task ExecuteAsync_MultilineWithinALineRange_MatchesAcrossTheRangesLines()
    {
        var resource = new ResourceKey("range.md");
        var path = Path.Combine(_tempFolder, "range.md");
        await File.WriteAllTextAsync(path, "a\nb\na\nb\na\nb\n");

        var command = CreateCommand();
        command.FileResource = resource;
        command.Pattern = "a\nb";
        command.Replacement = "X";
        command.Multiline = true;
        command.FromLine = 3;
        command.ToLine = 4;

        var result = await command.ExecuteAsync();

        result.IsSuccess.Should().BeTrue();
        command.ResultValue.ReplacementCount.Should().Be(1);
        var content = await File.ReadAllTextAsync(path);
        content.Should().Be("a\nb\nX\na\nb\n");
    }

    [Test]
    public async Task ExecuteAsync_FailsWhenAGroupReferenceNamesNoGroup()
    {
        var resource = new ResourceKey("group.md");
        var path = Path.Combine(_tempFolder, "group.md");
        await File.WriteAllTextAsync(path, "abc\n");

        var command = CreateCommand();
        command.FileResource = resource;
        command.Pattern = "(a)";
        command.Replacement = "$1st";
        command.Literal = false;

        var result = await command.ExecuteAsync();

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("${1}st");
        var content = await File.ReadAllTextAsync(path);
        content.Should().Be("abc\n");
    }

    [Test]
    public async Task ExecuteAsync_MergesSameLineHits_WhenLineContainsMultipleMatches()
    {
        // Three matches of "the" on line 1, one on line 2. Same-line hits on
        // line 1 collapse into a single entry with MatchCount=3; line 2 keeps
        // its own entry. Top-level ReplacementCount stays at the raw total 4.
        var resource = new ResourceKey("the.md");
        var path = Path.Combine(_tempFolder, "the.md");
        await File.WriteAllTextAsync(path, "the dog the cat the fox\nthe end\n");

        var command = CreateCommand();
        command.FileResource = resource;
        command.Pattern = "the";
        command.Replacement = "THE";
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
        var resource = new ResourceKey("scoped.md");
        var path = Path.Combine(_tempFolder, "scoped.md");
        await File.WriteAllTextAsync(path, "x\nx\nx\nx\nx\n");

        var command = CreateCommand();
        command.FileResource = resource;
        command.Pattern = "x";
        command.Replacement = "Y";
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
