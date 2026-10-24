namespace Celbridge.Tests.Utilities;

/// <summary>
/// Covers common agent filters for the search tools, and the rules where the filter differs from ripgrep's
/// --glob.
/// </summary>
[TestFixture]
public class SearchFilterTests
{
    private static SearchFilter Compile(string include, string exclude = "")
    {
        var compileResult = SearchFilter.Compile(include, exclude);
        compileResult.IsSuccess.Should().BeTrue(compileResult.FirstErrorMessage);

        return compileResult.Value;
    }

    [TestCase("*.py", "tools/build.py", true)]
    [TestCase("*.py", "build.py", true)]
    [TestCase("*.py", "notes.md", false)]
    [TestCase("*.{ts,tsx}", "src/app.ts", true)]
    [TestCase("*.{ts,tsx}", "src/view.tsx", true)]
    [TestCase("*.{ts,tsx}", "src/app.js", false)]
    [TestCase("src/**", "src/app/main.ts", true)]
    [TestCase("src/**", "lib/src/main.ts", false)]
    [TestCase("src/**/*.ts", "src/app/main.ts", true)]
    [TestCase("{src,lib}/*.ts", "lib/util.ts", true)]
    [TestCase("README.md", "docs/README.md", true)]
    [TestCase("/README.md", "docs/README.md", false)]
    public void Include_MatchesTheFilesAgentsMean(string include, string path, bool isMatch)
    {
        var filter = Compile(include);

        filter.IsMatch(path, isFolder: false).Should().Be(isMatch);
    }

    [Test]
    public void NoInclude_IncludesEveryFile()
    {
        var filter = Compile(string.Empty);

        filter.IsMatch("src/app.ts", isFolder: false).Should().BeTrue();
    }

    [Test]
    public void IncludeWithBang_Excludes()
    {
        var filter = Compile("!*.min.js");

        filter.IsMatch("src/app.js", isFolder: false).Should().BeTrue();
        filter.IsMatch("src/app.min.js", isFolder: false).Should().BeFalse();
    }

    [Test]
    public void CommasOutsideBraces_SeparatePatterns()
    {
        var filter = Compile("tests/, *.min.js");

        filter.IsMatch("tests/unit/app_test.py", isFolder: false).Should().BeTrue();
        filter.IsMatch("dist/app.min.js", isFolder: false).Should().BeTrue();
        filter.IsMatch("src/app.py", isFolder: false).Should().BeFalse();
    }

    [Test]
    public void EscapedComma_IsPartOfThePattern()
    {
        var filter = Compile(@"draft\,v2.md, *.txt");

        filter.IsMatch("draft,v2.md", isFolder: false).Should().BeTrue();
        filter.IsMatch("notes.txt", isFolder: false).Should().BeTrue();
        filter.IsMatch("v2.md", isFolder: false).Should().BeFalse();
    }

    [Test]
    public void ExcludeParameter_RemovesFromTheIncludes()
    {
        var filter = Compile("*.js", "dist/");

        filter.IsMatch("src/app.js", isFolder: false).Should().BeTrue();
        filter.IsMatch("dist/app.js", isFolder: false).Should().BeFalse();
    }

    [Test]
    public void APathUnderANarrowerFolder_StaysRelativeToTheRoot()
    {
        // A search of src still matches patterns against paths from the root.
        var filter = Compile("src/app/*.ts");

        filter.IsMatch("src/app/main.ts", isFolder: false).Should().BeTrue();
        filter.IsMatch("src/lib/main.ts", isFolder: false).Should().BeFalse();
    }

    [Test]
    public void LetterCase_IsIgnored()
    {
        var filter = Compile("*.PY", "BUILD/");

        filter.IsMatch("tools/run.py", isFolder: false).Should().BeTrue();
        filter.IsMatch("build/run.py", isFolder: false).Should().BeFalse();
    }

    [Test]
    public void IncludeOfAFolder_TakesTheFilesInside()
    {
        // ripgrep matches an include against a file's own path, so '-g tests/' finds nothing there.
        var filter = Compile("tests/");

        filter.IsMatch("tests/unit/app_test.py", isFolder: false).Should().BeTrue();
        filter.IsMatch("src/app.py", isFolder: false).Should().BeFalse();
    }

    [Test]
    public void IncludeOfTheFilesInAFolder_TakesEveryDepth()
    {
        // In ripgrep, '-g src/*' finds only the files directly in src.
        var filter = Compile("src/*");

        filter.IsMatch("src/app.ts", isFolder: false).Should().BeTrue();
        filter.IsMatch("src/app/main.ts", isFolder: false).Should().BeTrue();
    }

    [Test]
    public void Exclude_WinsWhateverTheOrder()
    {
        // In ripgrep, a later glob takes precedence over an earlier one.
        var filter = Compile("!*.min.js,*.js");

        filter.IsMatch("x.js", isFolder: false).Should().BeTrue();
        filter.IsMatch("x.min.js", isFolder: false).Should().BeFalse();
    }

    [Test]
    public void BangOrHashInsideBraces_IsLiteral()
    {
        var filter = Compile("{!notes,#draft}.md");

        filter.IsMatch("!notes.md", isFolder: false).Should().BeTrue();
        filter.IsMatch("#draft.md", isFolder: false).Should().BeTrue();
        filter.IsMatch("notes.md", isFolder: false).Should().BeFalse();
    }

    [Test]
    public void ExcludeWithBang_IsRefused()
    {
        var compileResult = SearchFilter.Compile(string.Empty, "!*.min.js");

        compileResult.IsFailure.Should().BeTrue();
        compileResult.FirstErrorMessage.Should().Contain("starts with '!'");
    }

    [Test]
    public void ARefusedPattern_FailsTheFilter()
    {
        var compileResult = SearchFilter.Compile(@"src\app\*.ts", string.Empty);

        compileResult.IsFailure.Should().BeTrue();
        compileResult.FirstErrorMessage.Should().Contain("Write '/' between folder names");
    }
}
