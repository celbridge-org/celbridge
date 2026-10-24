using System.Text.RegularExpressions;

namespace Celbridge.Tests.Utilities;

/// <summary>
/// Tests for GlobHelper, which converts glob patterns to regular expressions.
/// </summary>
[TestFixture]
public class GlobHelperTests
{
    //
    // GlobHelper.GlobToRegex — matches against a single name (no path separators)
    //

    [TestCase("*.cs", "foo.cs", true)]
    [TestCase("*.cs", "foo.py", false)]
    [TestCase("readme*", "readme", true)]
    [TestCase("readme*", "readme.md", true)]
    [TestCase("readme*", "readme.txt", true)]
    [TestCase("readme*", "README.md", false, TestName = "GlobToRegex_Star_CaseSensitiveByDefault")]
    [TestCase("readme.md", "readme.md", true)]
    [TestCase("readme.md", "readme.txt", false)]
    [TestCase("foo?.cs", "fooa.cs", true)]
    [TestCase("foo?.cs", "fooab.cs", false)]
    [TestCase("foo?.cs", "foo.cs", false, TestName = "GlobToRegex_Question_RequiresExactlyOneChar")]
    [TestCase("*", "anything", true)]
    [TestCase("*", "with.dot", true)]
    public void GlobToRegex_MatchesSingleName(string glob, string input, bool expectedMatch)
    {
        var pattern = GlobHelper.GlobToRegex(glob);
        var regex = new Regex(pattern);

        regex.IsMatch(input).Should().Be(expectedMatch);
    }

    [Test]
    public void GlobToRegex_IsNotCaseInsensitiveByDefault()
    {
        var pattern = GlobHelper.GlobToRegex("*.CS");
        var caseSensitiveRegex = new Regex(pattern);
        var caseInsensitiveRegex = new Regex(pattern, RegexOptions.IgnoreCase);

        caseSensitiveRegex.IsMatch("foo.cs").Should().BeFalse();
        caseInsensitiveRegex.IsMatch("foo.cs").Should().BeTrue();
    }

    //
    // GlobHelper.PathGlobToRegex — matches against a full resource key path
    //

    [TestCase("*.py", "foo.py", true)]
    [TestCase("*.py", "src/foo.py", true, TestName = "PathGlobToRegex_StarExtension_MatchesAtAnyDepth")]
    [TestCase("*.py", "a/b/foo.py", true, TestName = "PathGlobToRegex_StarExtension_MatchesDeepNesting")]
    [TestCase("*.py", "foo.txt", false, TestName = "PathGlobToRegex_StarExtension_RequiresExtensionMatch")]
    [TestCase("readme", "readme", true, TestName = "PathGlobToRegex_BareName_MatchesTopLevel")]
    [TestCase("readme", "src/readme", true, TestName = "PathGlobToRegex_BareName_MatchesAtAnyDepth")]
    [TestCase("src/*.cs", "src/foo.cs", true)]
    [TestCase("src/*.cs", "src/sub/foo.cs", false, TestName = "PathGlobToRegex_Star_NoMatchAcrossSlash")]
    [TestCase("src/*.cs", "other/foo.cs", false)]
    [TestCase("**/foo.cs", "foo.cs", true, TestName = "PathGlobToRegex_DoubleStarSlash_MatchesTopLevel")]
    [TestCase("**/foo.cs", "src/foo.cs", true, TestName = "PathGlobToRegex_DoubleStarSlash_MatchesOneLevel")]
    [TestCase("**/foo.cs", "src/sub/foo.cs", true, TestName = "PathGlobToRegex_DoubleStarSlash_MatchesTwoLevels")]
    [TestCase("**/foo.cs", "src/foo.txt", false)]
    [TestCase("**/Commands/*.cs", "Commands/Foo.cs", true, TestName = "PathGlobToRegex_DoubleStarSlash_TopLevelFolder")]
    [TestCase("**/Commands/*.cs", "src/Commands/Foo.cs", true, TestName = "PathGlobToRegex_DoubleStarSlash_NestedFolder")]
    [TestCase("**/Commands/*.cs", "src/Commands/sub/Foo.cs", false, TestName = "PathGlobToRegex_DoubleStarSlash_DoesNotMatchAcrossSlashAfterStar")]
    [TestCase("Services/**/I*.cs", "Services/IFoo.cs", true, TestName = "PathGlobToRegex_DoubleStarMid_TopLevelFile")]
    [TestCase("Services/**/I*.cs", "Services/sub/IFoo.cs", true, TestName = "PathGlobToRegex_DoubleStarMid_NestedFile")]
    [TestCase("Services/**/I*.cs", "Services/sub/deep/IFoo.cs", true, TestName = "PathGlobToRegex_DoubleStarMid_DeepNestedFile")]
    [TestCase("Services/**/I*.cs", "Other/IFoo.cs", false)]
    [TestCase("**", "anything/at/all.cs", true)]
    [TestCase("src/**", "src/foo.cs", true)]
    [TestCase("src/**", "src/sub/foo.cs", true)]
    [TestCase("src/**", "other/foo.cs", false)]
    public void PathGlobToRegex_MatchesResourcePath(string glob, string input, bool expectedMatch)
    {
        var pattern = GlobHelper.PathGlobToRegex(glob);
        var regex = new Regex(pattern, RegexOptions.IgnoreCase);

        regex.IsMatch(input).Should().Be(expectedMatch);
    }
}
