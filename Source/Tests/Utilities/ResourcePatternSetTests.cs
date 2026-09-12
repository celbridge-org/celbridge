using Celbridge.Utilities;

namespace Celbridge.Tests.Utilities;

/// <summary>
/// Covers the pattern set's own job: evaluating several patterns together, and taking the subtree of
/// any folder one of them matches.
/// </summary>
[TestFixture]
public class ResourcePatternSetTests
{
    [Test]
    public void EmptySet_MatchesNothing()
    {
        var patterns = ResourcePatternSet.Compile(Array.Empty<string>());

        patterns.IsMatch("src/main.py", isFolder: false).Should().BeFalse();
        patterns.IsMatch("src", isFolder: true).Should().BeFalse();
    }

    [Test]
    public void BlankEntries_AreDropped()
    {
        // A trailing newline in the settings text box arrives as a blank entry, which would otherwise
        // compile into a pattern that matches everything or nothing depending on the matcher.
        var patterns = ResourcePatternSet.Compile(new[] { "", "   ", "bin" });

        patterns.IsMatch("bin/app.exe", isFolder: false).Should().BeTrue();
        patterns.IsMatch("src/main.py", isFolder: false).Should().BeFalse();
    }

    [Test]
    public void EmptyPath_MatchesNothing()
    {
        // The project root has an empty path, and no pattern should take the whole project with it.
        var patterns = ResourcePatternSet.Compile(new[] { "**" });

        patterns.IsMatch(string.Empty, isFolder: true).Should().BeFalse();
    }

    [Test]
    public void BareNamePattern_TakesTheSubtreeAtAnyDepth()
    {
        var patterns = ResourcePatternSet.Compile(new[] { "bin" });

        patterns.IsMatch("bin", isFolder: true).Should().BeTrue();
        patterns.IsMatch("bin/app.exe", isFolder: false).Should().BeTrue();
        patterns.IsMatch("src/bin/app.exe", isFolder: false).Should().BeTrue();
        patterns.IsMatch("src/bin/nested/deep.txt", isFolder: false).Should().BeTrue();
        patterns.IsMatch("src/binary.txt", isFolder: false).Should().BeFalse();
    }

    [Test]
    public void PathPattern_IsAnchoredAndTakesItsSubtree()
    {
        var patterns = ResourcePatternSet.Compile(new[] { "src/obj" });

        patterns.IsMatch("src/obj", isFolder: true).Should().BeTrue();
        patterns.IsMatch("src/obj/build.log", isFolder: false).Should().BeTrue();
        patterns.IsMatch("src/obj/nested/deep.log", isFolder: false).Should().BeTrue();

        // Anchored at the project root, so the same folder name elsewhere is untouched.
        patterns.IsMatch("obj/build.log", isFolder: false).Should().BeFalse();
        patterns.IsMatch("other/src/obj/build.log", isFolder: false).Should().BeFalse();
    }

    [Test]
    public void WildcardPattern_TakesTheSubtreeOfTheFolderItMatches()
    {
        var patterns = ResourcePatternSet.Compile(new[] { "**/cache" });

        patterns.IsMatch("cache/entry.bin", isFolder: false).Should().BeTrue();
        patterns.IsMatch("src/cache/entry.bin", isFolder: false).Should().BeTrue();
        patterns.IsMatch("src/cache/nested/entry.bin", isFolder: false).Should().BeTrue();
        patterns.IsMatch("src/main.py", isFolder: false).Should().BeFalse();
    }

    [Test]
    public void FolderOnlyPattern_SkipsAFileOfTheSameName()
    {
        var patterns = ResourcePatternSet.Compile(new[] { "dist/" });

        patterns.IsMatch("dist", isFolder: true).Should().BeTrue();
        patterns.IsMatch("dist/bundle.js", isFolder: false).Should().BeTrue();

        // A file named dist is not the folder the pattern reserved.
        patterns.IsMatch("dist", isFolder: false).Should().BeFalse();
    }

    [Test]
    public void FolderOnlyPathPattern_StillTakesItsSubtree()
    {
        // The subtree is reached through the ancestor walk, which tests the prefix as a folder even
        // though the resource itself is a file.
        var patterns = ResourcePatternSet.Compile(new[] { "src/obj/" });

        patterns.IsMatch("src/obj", isFolder: true).Should().BeTrue();
        patterns.IsMatch("src/obj/build.log", isFolder: false).Should().BeTrue();
        patterns.IsMatch("src/obj", isFolder: false).Should().BeFalse();
    }

    [Test]
    public void AnyPatternInTheSet_CanMatch()
    {
        var patterns = ResourcePatternSet.Compile(new[] { "bin", "src/obj", "dist/" });

        patterns.IsMatch("bin/app.exe", isFolder: false).Should().BeTrue();
        patterns.IsMatch("src/obj/build.log", isFolder: false).Should().BeTrue();
        patterns.IsMatch("dist/bundle.js", isFolder: false).Should().BeTrue();
        patterns.IsMatch("src/main.py", isFolder: false).Should().BeFalse();
    }

    [Test]
    public void TrailingSlashInThePath_DoesNotLoop()
    {
        // Resource paths carry no trailing slash, but the ancestor walk must terminate on one rather
        // than spinning on a zero-width final segment.
        var patterns = ResourcePatternSet.Compile(new[] { "src/obj" });

        patterns.IsMatch("src/obj/", isFolder: true).Should().BeTrue();
        patterns.IsMatch("src/other/", isFolder: true).Should().BeFalse();
    }
}
