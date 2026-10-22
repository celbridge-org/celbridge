using Celbridge.Utilities;

namespace Celbridge.Tests.Utilities;

[TestFixture]
public class ResourcePathMatcherTests
{
    [Test]
    public void BareName_MatchesAtAnyDepth()
    {
        var matcher = ResourcePathMatcher.Compile("build");

        matcher.IsMatch("build", isFolder: true).Should().BeTrue();
        matcher.IsMatch("build", isFolder: false).Should().BeTrue();
        matcher.IsMatch("dir/build", isFolder: true).Should().BeTrue();
        matcher.IsMatch("dir/sub/build", isFolder: true).Should().BeTrue();
        matcher.IsMatch("build/foo.txt", isFolder: false).Should().BeTrue();
        matcher.IsMatch("notbuild", isFolder: false).Should().BeFalse();
        matcher.IsMatch("foo.build", isFolder: false).Should().BeFalse();
    }

    [Test]
    public void IgnoreCase_MatchesAnyLetterCase()
    {
        var caseSensitive = ResourcePathMatcher.Compile("build");
        var ignoringCase = ResourcePathMatcher.Compile("build", ignoreCase: true);

        caseSensitive.IsMatch("dir/BUILD", isFolder: true).Should().BeFalse();
        ignoringCase.IsMatch("dir/BUILD", isFolder: true).Should().BeTrue();
        ignoringCase.IsMatch("Build/foo.txt", isFolder: false).Should().BeTrue();
    }

    [Test]
    public void SingleStar_MatchesOneSegment()
    {
        var matcher = ResourcePathMatcher.Compile("*.log");

        matcher.IsMatch("foo.log", isFolder: false).Should().BeTrue();
        matcher.IsMatch("dir/foo.log", isFolder: false).Should().BeTrue();
        matcher.IsMatch("dir/sub/foo.log", isFolder: false).Should().BeTrue();
        matcher.IsMatch("foo.log.txt", isFolder: false).Should().BeFalse();
        matcher.IsMatch("foolog", isFolder: false).Should().BeFalse();
    }

    [Test]
    public void DoubleStar_MatchesAcrossPathSeparators()
    {
        var matcher = ResourcePathMatcher.Compile("docs/api/**");

        matcher.IsMatch("docs/api/index.md", isFolder: false).Should().BeTrue();
        matcher.IsMatch("docs/api/sub/file.md", isFolder: false).Should().BeTrue();
        matcher.IsMatch("docs/api", isFolder: true).Should().BeFalse();
        matcher.IsMatch("docs/other/file.md", isFolder: false).Should().BeFalse();
    }

    [Test]
    public void DoubleStarPrefix_MatchesAtAnyDepth_OnSegmentBoundary()
    {
        var matcher = ResourcePathMatcher.Compile("**/secret");

        matcher.IsMatch("secret", isFolder: false).Should().BeTrue();
        matcher.IsMatch("a/secret", isFolder: false).Should().BeTrue();
        matcher.IsMatch("a/b/secret", isFolder: false).Should().BeTrue();
        // '**/' spans whole segments, so a partial-name match is not a match.
        matcher.IsMatch("notsecret", isFolder: false).Should().BeFalse();
        matcher.IsMatch("a/notsecret", isFolder: false).Should().BeFalse();
    }

    [Test]
    public void DoubleStarInMiddle_MatchesWholeSegmentsOnly()
    {
        var matcher = ResourcePathMatcher.Compile("a/**/b");

        matcher.IsMatch("a/b", isFolder: false).Should().BeTrue();
        matcher.IsMatch("a/x/b", isFolder: false).Should().BeTrue();
        matcher.IsMatch("a/x/y/b", isFolder: false).Should().BeTrue();
        // The separator before 'b' is required, so 'a/xb' is not a match.
        matcher.IsMatch("a/xb", isFolder: false).Should().BeFalse();
    }

    [Test]
    public void SlashedPattern_AnchoredAtRoot()
    {
        var matcher = ResourcePathMatcher.Compile("docs/draft");

        matcher.IsMatch("docs/draft", isFolder: true).Should().BeTrue();
        matcher.IsMatch("docs/draft", isFolder: false).Should().BeTrue();
        matcher.IsMatch("other/docs/draft", isFolder: true).Should().BeFalse();
    }

    [Test]
    public void TrailingSlash_RestrictsToFolders()
    {
        var matcher = ResourcePathMatcher.Compile("build/");

        matcher.IsMatch("build", isFolder: true).Should().BeTrue();
        matcher.IsMatch("build", isFolder: false).Should().BeFalse();
        matcher.IsMatch("build/foo.txt", isFolder: false).Should().BeTrue();
    }

    [Test]
    public void WildcardStar_MatchesEverySegment()
    {
        var matcher = ResourcePathMatcher.Compile("*");

        matcher.IsMatch("foo", isFolder: false).Should().BeTrue();
        matcher.IsMatch("foo/bar", isFolder: false).Should().BeTrue();
        matcher.IsMatch("very/deep/nested/path", isFolder: false).Should().BeTrue();
    }

    [Test]
    public void Anchored_BareNameMatchesOneTopLevelItem()
    {
        var matcher = ResourcePathMatcher.Compile("readme.md", anchored: true);

        matcher.MatchesAtAnyDepth.Should().BeFalse();
        matcher.IsMatch("readme.md", isFolder: false).Should().BeTrue();
        matcher.IsMatch("docs/readme.md", isFolder: false).Should().BeFalse();
        matcher.IsMatch("readme.md/child.txt", isFolder: false).Should().BeFalse();
    }

    [Test]
    public void Anchored_DoubleStarMatchesAnyNumberOfFolders()
    {
        var matcher = ResourcePathMatcher.Compile("**/*.cs", anchored: true);

        matcher.IsMatch("Program.cs", isFolder: false).Should().BeTrue();
        matcher.IsMatch("src/Program.cs", isFolder: false).Should().BeTrue();
        matcher.IsMatch("src/app/Program.cs", isFolder: false).Should().BeTrue();
        matcher.IsMatch("src/Program.csx", isFolder: false).Should().BeFalse();
    }

    [Test]
    public void Anchored_SingleStarStaysInOneFolder()
    {
        var matcher = ResourcePathMatcher.Compile("docs/*", anchored: true);

        matcher.IsMatch("docs/a.md", isFolder: false).Should().BeTrue();
        matcher.IsMatch("docs/sub/a.md", isFolder: false).Should().BeFalse();
    }

    [Test]
    public void CanMatchInside_FollowsTheFoldersThePatternNames()
    {
        var matcher = ResourcePathMatcher.Compile("src/app/*.cs", anchored: true);

        matcher.CanMatchInside(string.Empty).Should().BeTrue();
        matcher.CanMatchInside("src").Should().BeTrue();
        matcher.CanMatchInside("src/app").Should().BeTrue();
        matcher.CanMatchInside("src/app/sub").Should().BeFalse();
        matcher.CanMatchInside("src/other").Should().BeFalse();
        matcher.CanMatchInside("docs").Should().BeFalse();
    }

    [Test]
    public void CanMatchInside_DoubleStarMatchesBelowEveryFolderUnderIt()
    {
        var anywhere = ResourcePathMatcher.Compile("**/*.cs", anchored: true);
        var underSource = ResourcePathMatcher.Compile("src/**", anchored: true);

        anywhere.CanMatchInside("docs/deep/folder").Should().BeTrue();
        underSource.CanMatchInside("src").Should().BeTrue();
        underSource.CanMatchInside("src/a/b").Should().BeTrue();
        underSource.CanMatchInside("docs").Should().BeFalse();
    }

    [Test]
    public void CanMatchInside_IsFalseForThePathThePatternNames()
    {
        var matcher = ResourcePathMatcher.Compile("docs/readme.md", anchored: true);

        matcher.CanMatchInside("docs").Should().BeTrue();
        matcher.CanMatchInside("docs/readme.md").Should().BeFalse();
    }
}
