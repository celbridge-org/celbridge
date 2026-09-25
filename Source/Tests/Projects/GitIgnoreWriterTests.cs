using Celbridge.Projects;
using Celbridge.Tests.FileSystem;

namespace Celbridge.Tests.Projects;

[TestFixture]
public class GitIgnoreWriterTests
{
    private const string CelbridgeContents = "# Celbridge project metadata\n.celbridge/\n\n# Build output\nbin/\nobj/\n";

    private ILocalFileSystem _fileSystem = null!;
    private string _tempRootPath = null!;

    [SetUp]
    public void Setup()
    {
        _fileSystem = TestFileSystem.CreateLocal();

        _tempRootPath = Path.Combine(
            Path.GetTempPath(),
            "celbridge-gitignore-tests",
            Path.GetFileNameWithoutExtension(Path.GetRandomFileName()));
        Directory.CreateDirectory(_tempRootPath);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempRootPath))
        {
            Directory.Delete(_tempRootPath, recursive: true);
        }
    }

    [Test]
    public void Merge_ExistingPatterns_AppendsOnlyTheMissingOnes()
    {
        var existingContents = "# The user's rules\nbin/\n*.log\n";

        var mergedContents = GitIgnoreWriter.Merge(existingContents, CelbridgeContents);

        mergedContents.Should().NotBeNull();

        // The user's file is kept whole and the block goes after it, so their rules read first.
        mergedContents!.Should().StartWith(existingContents);
        mergedContents.Should().Be(existingContents + "\n# Added by Celbridge\n.celbridge/\nobj/\n");
    }

    [Test]
    public void Merge_EveryPatternAlreadyPresent_ReturnsNull()
    {
        var existingContents = "obj/\nbin/\n.celbridge/\n";

        var mergedContents = GitIgnoreWriter.Merge(existingContents, CelbridgeContents);

        // Nothing to add, so the file is left exactly as it was rather than rewritten.
        mergedContents.Should().BeNull();
    }

    [Test]
    public void Merge_CommentsAndBlankLines_AreNotPatterns()
    {
        // A comment naming a pattern does not declare it, so the pattern is still missing. Asserting
        // the whole result matters here: the existing '# bin/' line contains the text 'bin/' itself,
        // so a Contain check would hold whether or not the pattern reached the block.
        var existingContents = "# bin/\n\n   \n";

        var mergedContents = GitIgnoreWriter.Merge(existingContents, CelbridgeContents);

        mergedContents.Should().NotBeNull();
        mergedContents!.Should().Be(existingContents + "\n# Added by Celbridge\n.celbridge/\nbin/\nobj/\n");
    }

    [Test]
    public void Merge_ExistingBlock_IsRewrittenRatherThanRepeated()
    {
        // The block a previous release wrote, against a canonical file that has gained obj/ since.
        var existingContents = "*.log\n\n# Added by Celbridge\n.celbridge/\nbin/\n";

        var mergedContents = GitIgnoreWriter.Merge(existingContents, CelbridgeContents);

        mergedContents.Should().NotBeNull();
        mergedContents!.Should().Be("*.log\n\n# Added by Celbridge\n.celbridge/\nbin/\nobj/\n");
    }

    [Test]
    public void Merge_ExistingBlock_KeepsLinesAddedInsideIt()
    {
        var existingContents = "# Added by Celbridge\n.celbridge/\nbin/\nobj/\nmy-own-rule/\n";
        var laterCelbridgeContents = CelbridgeContents + "\n# Tool output\n.ruff_cache/\n";

        var mergedContents = GitIgnoreWriter.Merge(existingContents, laterCelbridgeContents);

        // A line Celbridge does not ship was typed in by hand, so the rewrite carries it over.
        mergedContents.Should().NotBeNull();
        mergedContents!.Should().Be("# Added by Celbridge\n.celbridge/\nbin/\nobj/\n.ruff_cache/\nmy-own-rule/\n");
    }

    [Test]
    public void Merge_ExistingBlock_AlreadyCurrent_ReturnsNull()
    {
        var existingContents = "*.log\n\n# Added by Celbridge\n.celbridge/\nbin/\nobj/\n";

        var mergedContents = GitIgnoreWriter.Merge(existingContents, CelbridgeContents);

        mergedContents.Should().BeNull();
    }

    [Test]
    public void Merge_PatternMovedOutOfTheBlock_IsNotPutBack()
    {
        // The user moved bin/ up into their own rules. The block does not claim it a second time.
        var existingContents = "bin/\n\n# Added by Celbridge\n.celbridge/\nobj/\n";

        var mergedContents = GitIgnoreWriter.Merge(existingContents, CelbridgeContents);

        mergedContents.Should().BeNull();
    }

    [Test]
    public void Merge_CrlfFile_KeepsCrlfEndings()
    {
        var existingContents = "# The user's rules\r\n*.log\r\n";

        var mergedContents = GitIgnoreWriter.Merge(existingContents, CelbridgeContents);

        mergedContents.Should().NotBeNull();
        mergedContents!.Should().Be(existingContents + "\r\n# Added by Celbridge\r\n.celbridge/\r\nbin/\r\nobj/\r\n");
    }

    [Test]
    public void Merge_NoTrailingNewline_StartsTheBlockOnItsOwnLine()
    {
        var existingContents = "*.log";

        var mergedContents = GitIgnoreWriter.Merge(existingContents, CelbridgeContents);

        mergedContents.Should().NotBeNull();
        mergedContents!.Should().Be("*.log\n\n# Added by Celbridge\n.celbridge/\nbin/\nobj/\n");
    }

    [Test]
    public void Merge_EmptyFile_AppendsWithNoLeadingBlankLine()
    {
        var mergedContents = GitIgnoreWriter.Merge(string.Empty, CelbridgeContents);

        mergedContents.Should().NotBeNull();
        mergedContents!.Should().StartWith("# Added by Celbridge\n");
    }

    [Test]
    public async Task WriteAsync_NoExistingFile_WritesCelbridgeGitIgnore()
    {
        var gitIgnorePath = Path.Combine(_tempRootPath, ".gitignore");

        var result = await GitIgnoreWriter.WriteAsync(_tempRootPath, _fileSystem);

        result.IsSuccess.Should().BeTrue();

        // The bundled resource is what lands, so a fresh file carries no merge marker.
        var gitIgnoreContents = await File.ReadAllTextAsync(gitIgnorePath);
        gitIgnoreContents.Should().Contain(".celbridge/");
        gitIgnoreContents.Should().Contain("/downloads/");
        gitIgnoreContents.Should().NotContain("# Added by Celbridge");
    }

    [Test]
    public async Task WriteAsync_RunTwice_AddsTheBlockOnce()
    {
        var gitIgnorePath = Path.Combine(_tempRootPath, ".gitignore");
        await File.WriteAllTextAsync(gitIgnorePath, "*.log\n");

        await GitIgnoreWriter.WriteAsync(_tempRootPath, _fileSystem);
        var afterFirstWrite = await File.ReadAllTextAsync(gitIgnorePath);

        await GitIgnoreWriter.WriteAsync(_tempRootPath, _fileSystem);
        var afterSecondWrite = await File.ReadAllTextAsync(gitIgnorePath);

        // Creating a second project in the same folder must not stack up another block.
        afterSecondWrite.Should().Be(afterFirstWrite);
        afterSecondWrite.Should().Contain("*.log");
    }

    [Test]
    public async Task WriteAsync_StaleBlock_IsBroughtUpToDateInPlace()
    {
        var gitIgnorePath = Path.Combine(_tempRootPath, ".gitignore");

        // A block from an older release, holding one of the patterns the canonical file ships today.
        await File.WriteAllTextAsync(gitIgnorePath, "*.log\n\n# Added by Celbridge\n.celbridge/\n");

        var result = await GitIgnoreWriter.WriteAsync(_tempRootPath, _fileSystem);

        result.IsSuccess.Should().BeTrue();

        var gitIgnoreContents = await File.ReadAllTextAsync(gitIgnorePath);

        // The patterns the file was missing land in the block it already had, not in a second one.
        var blockCount = gitIgnoreContents.Split("# Added by Celbridge").Length - 1;
        blockCount.Should().Be(1);

        gitIgnoreContents.Should().StartWith("*.log\n");
        gitIgnoreContents.Should().Contain("/downloads/");
    }
}
