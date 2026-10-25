namespace Celbridge.Tests.Patterns;

/// <summary>
/// Covers refused lines, letter case and CanMatchInside. GitignoreMatcherGitComparisonTests covers how lines
/// match.
/// </summary>
[TestFixture]
public class GitignoreMatcherTests
{
    private static GitignoreMatcher Compile(params string[] lines)
    {
        var compileResult = GitignoreMatcher.Compile(lines, ignoreCase: false);
        compileResult.IsSuccess.Should().BeTrue(compileResult.FirstErrorMessage);

        return compileResult.Value;
    }

    private static string CompileError(params string[] lines)
    {
        var compileResult = GitignoreMatcher.Compile(lines, ignoreCase: false);
        compileResult.IsFailure.Should().BeTrue();

        return compileResult.FirstErrorMessage;
    }

    [Test]
    public void BackslashBeforeALetter_IsRefused()
    {
        // Git drops the '\', so a Windows path would become one name.
        var message = CompileError(@"src\bin");

        message.Should().Contain(@"'\b' is not an escape");
        message.Should().Contain("Write '/' between folder names");
    }

    [TestCase(@"backend\.venv", Description = "a Windows path to a dot folder")]
    [TestCase(@"src\/bin", Description = "an escaped slash")]
    [TestCase(@"\d+.log", Description = "a regex class")]
    [TestCase(@"[\a]", Description = "a letter in a class")]
    public void BackslashBeforeAnOrdinaryCharacter_IsRefused(string line)
    {
        var message = CompileError(line);

        message.Should().Contain("is not an escape");
    }

    [Test]
    public void TrailingBackslash_IsRefused()
    {
        var message = CompileError(@"notes\");

        message.Should().Contain(@"ends in '\'");
        message.Should().Contain(@"'\\'");
    }

    [TestCase("data[12.csv")]
    [TestCase("[]")]
    [TestCase("[!]")]
    [TestCase("[[:alpha:]")]
    public void UnclosedBracket_IsRefused(string line)
    {
        var message = CompileError(line);

        message.Should().Contain("has no closing ']'");
        message.Should().Contain(@"'\['");
    }

    [Test]
    public void UnknownNamedClass_IsRefused()
    {
        var message = CompileError("[[:letter:]]*");

        message.Should().Contain("'[:letter:]' is not a character class");
    }

    [TestCase("*.{js,ts}")]
    [TestCase("notes}")]
    public void Braces_AreRefused(string line)
    {
        var message = CompileError(line);

        message.Should().Contain("uses braces");
        message.Should().Contain("separate pattern for each alternative");
    }

    [Test]
    public void EscapedSpecialCharactersAndBracesInAClass_AreAccepted()
    {
        var matcher = Compile(@"\{draft\}.md", "[{]notes[}].md", @"draft\,v2.md", @"my\ file.txt", @"note[\-_]s.md");

        matcher.IsMatch("{draft}.md", isFolder: false).Should().BeTrue();
        matcher.IsMatch("{notes}.md", isFolder: false).Should().BeTrue();
        matcher.IsMatch("draft,v2.md", isFolder: false).Should().BeTrue();
        matcher.IsMatch("my file.txt", isFolder: false).Should().BeTrue();
        matcher.IsMatch("note-s.md", isFolder: false).Should().BeTrue();
    }

    [Test]
    public void ARefusedLine_FailsTheWholeList()
    {
        var message = CompileError("bin", @"src\obj");

        message.Should().Contain(@"src\obj");
    }

    [Test]
    public void BlankLinesAndComments_MatchNothing()
    {
        var matcher = Compile(string.Empty, "   ", "# bin");

        matcher.IsMatch("bin", isFolder: true).Should().BeFalse();
        matcher.IsMatch("# bin", isFolder: false).Should().BeFalse();
    }

    [Test]
    public void TheRoot_IsNeverMatched()
    {
        var matcher = Compile("*");

        matcher.IsMatch(string.Empty, isFolder: true).Should().BeFalse();
    }

    [Test]
    public void LetterCase_IsExactUnlessIgnored()
    {
        var exactCase = Compile(".git");
        var ignoringCase = GitignoreMatcher.Compile([".git"], ignoreCase: true).Value;

        exactCase.IsMatch(".GIT/config", isFolder: false).Should().BeFalse();
        ignoringCase.IsMatch(".GIT/config", isFolder: false).Should().BeTrue();
        ignoringCase.IsMatch("src/.Git", isFolder: true).Should().BeTrue();
    }

    [Test]
    public void ManyStars_MatchInLinearTime()
    {
        // A backtracking engine takes seconds on this line and name, and each extra star multiplies the time.
        var matcher = Compile("*a*a*a*a*a*a*a*a*b");
        var name = new string('a', 40) + ".txt";

        var matchTask = Task.Run(() => matcher.IsMatch(name, isFolder: false));

        matchTask.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue("the match should finish within 2 seconds");
        matchTask.Result.Should().BeFalse();
    }

    [Test]
    public void CanMatchInside_AnchoredPattern_RulesOutOtherFolders()
    {
        var matcher = Compile("/src/*.py");

        matcher.CanMatchInside(string.Empty).Should().BeTrue();
        matcher.CanMatchInside("src").Should().BeTrue();
        matcher.CanMatchInside("docs").Should().BeFalse();
    }

    [Test]
    public void CanMatchInside_NameAtAnyDepth_CanMatchInsideEveryFolder()
    {
        var matcher = Compile("*.py");

        matcher.CanMatchInside("docs/api").Should().BeTrue();
    }

    [Test]
    public void CanMatchInside_MatchedFolder_TakesEverythingInside()
    {
        var matcher = Compile("/docs/");

        matcher.CanMatchInside("docs").Should().BeTrue();
        matcher.CanMatchInside("docs/api/v2").Should().BeTrue();
        matcher.CanMatchInside("src").Should().BeFalse();
    }

    [Test]
    public void CanMatchInside_DoubleStar_FollowsAnyNumberOfFolders()
    {
        var matcher = Compile("a/**/b");

        matcher.CanMatchInside("a/x/y").Should().BeTrue();
        matcher.CanMatchInside("c").Should().BeFalse();
    }

    [Test]
    public void CanMatchInside_OnlyLinesThatPutPathsBack_MatchNothing()
    {
        var matcher = Compile("!*.py");

        matcher.CanMatchInside(string.Empty).Should().BeFalse();
    }
}
