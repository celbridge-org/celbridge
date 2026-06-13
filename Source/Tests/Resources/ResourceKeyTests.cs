namespace Celbridge.Tests.Resources;

[TestFixture]
public class ResourceKeyTests
{
    [Test]
    public void ICanValidateResourceKeys()
    {
        // Check valid paths pass
        ResourceKey.IsValidSegment("ValidSegment").Should().BeTrue();
        ResourceKey.IsValidKey(@"Some/Path/File.txt").Should().BeTrue();

        // Check invalid paths fail
        ResourceKey.IsValidSegment("Invalid\0Segment").Should().BeFalse();
        ResourceKey.IsValidKey(@"C:\\AbsolutePath").Should().BeFalse();
        ResourceKey.IsValidKey(@"\AbsolutePath").Should().BeFalse();
        ResourceKey.IsValidKey(@"/Some/Path/File.txt").Should().BeFalse();
        ResourceKey.IsValidKey(@"Some/Path/File.txt/").Should().BeFalse();
    }

    [Test]
    public void ConstructorThrowsOnInvalidKey()
    {
        // Valid keys should not throw
        var validKey = new ResourceKey("Some/Path/File.txt");
        validKey.ToString().Should().Be("project:Some/Path/File.txt");

        // Empty key is valid
        var emptyKey = new ResourceKey("");
        emptyKey.IsEmpty.Should().BeTrue();

        // Invalid keys should throw ArgumentException
        var act1 = () => new ResourceKey(@"C:\AbsolutePath");
        act1.Should().Throw<ArgumentException>().WithParameterName("key");

        var act2 = () => new ResourceKey("/Some/Path/File.txt");
        act2.Should().Throw<ArgumentException>().WithParameterName("key");

        var act3 = () => new ResourceKey("Some/Path/File.txt/");
        act3.Should().Throw<ArgumentException>().WithParameterName("key");

        var act4 = () => new ResourceKey("../escape");
        act4.Should().Throw<ArgumentException>().WithParameterName("key");

        var act5 = () => new ResourceKey("folder//file");
        act5.Should().Throw<ArgumentException>().WithParameterName("key");

        var act6 = () => new ResourceKey(@"folder\file");
        act6.Should().Throw<ArgumentException>().WithParameterName("key");

        // Whitespace-only keys should throw
        var act7 = () => new ResourceKey(" ");
        act7.Should().Throw<ArgumentException>().WithParameterName("key");

        var act8 = () => new ResourceKey("\t");
        act8.Should().Throw<ArgumentException>().WithParameterName("key");
    }

    [Test]
    public void ImplicitConversionThrowsOnInvalidKey()
    {
        // Valid string converts successfully
        ResourceKey key = "Some/Path/File.txt";
        key.ToString().Should().Be("project:Some/Path/File.txt");

        // Invalid string throws
        var act = () => { ResourceKey invalid = "../escape"; };
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void CreateThrowsOnInvalidKey()
    {
        // Valid keys should not throw
        var validKey = ResourceKey.Create("Some/Path/File.txt");
        validKey.ToString().Should().Be("project:Some/Path/File.txt");

        // Empty key is valid
        var emptyKey = ResourceKey.Create("");
        emptyKey.IsEmpty.Should().BeTrue();

        // Invalid keys should throw ArgumentException
        var act1 = () => ResourceKey.Create(@"C:\AbsolutePath");
        act1.Should().Throw<ArgumentException>().WithParameterName("key");

        var act2 = () => ResourceKey.Create("/Some/Path/File.txt");
        act2.Should().Throw<ArgumentException>().WithParameterName("key");

        var act3 = () => ResourceKey.Create("Some/Path/File.txt/");
        act3.Should().Throw<ArgumentException>().WithParameterName("key");
    }

    [Test]
    public void TryCreateReturnsFalseOnInvalidKey()
    {
        // Valid keys should succeed
        ResourceKey.TryCreate("Some/Path/File.txt", out var validKey).Should().BeTrue();
        validKey.ToString().Should().Be("project:Some/Path/File.txt");

        // Empty key is valid
        ResourceKey.TryCreate("", out var emptyKey).Should().BeTrue();
        emptyKey.IsEmpty.Should().BeTrue();

        // Invalid keys should return false without throwing
        ResourceKey.TryCreate(@"C:\AbsolutePath", out var invalidKey1).Should().BeFalse();
        invalidKey1.IsEmpty.Should().BeTrue();

        ResourceKey.TryCreate("/Some/Path/File.txt", out var invalidKey2).Should().BeFalse();
        invalidKey2.IsEmpty.Should().BeTrue();

        ResourceKey.TryCreate("Some/Path/File.txt/", out var invalidKey3).Should().BeFalse();
        invalidKey3.IsEmpty.Should().BeTrue();

        ResourceKey.TryCreate("../escape", out var invalidKey4).Should().BeFalse();
        invalidKey4.IsEmpty.Should().BeTrue();
    }

    [Test]
    public void IsDescendantOfWorksCorrectly()
    {
        var childKey = new ResourceKey("folder/subfolder/file.txt");

        // Normal case
        childKey.IsDescendantOf(new ResourceKey("folder")).Should().BeTrue();
        childKey.IsDescendantOf(new ResourceKey("folder/subfolder")).Should().BeTrue();

        // Non-descendants
        childKey.IsDescendantOf(new ResourceKey("other")).Should().BeFalse();
        childKey.IsDescendantOf(new ResourceKey("fold")).Should().BeFalse();
    }

    [Test]
    public void GetParentReturnsParentFolder()
    {
        // Nested path returns parent folder
        new ResourceKey("a/b/file.txt").GetParent().ToString().Should().Be("project:a/b");

        // Deeply nested path
        new ResourceKey("a/b/c/d/file.txt").GetParent().ToString().Should().Be("project:a/b/c/d");

        // Root-level file returns empty
        new ResourceKey("file.txt").GetParent().ToString().Should().Be("project:");

        // Empty key returns empty
        ResourceKey.Empty.GetParent().ToString().Should().Be("project:");

        // Path with spaces in segments
        new ResourceKey("My Docs/My File.txt").GetParent().ToString().Should().Be("project:My Docs");

        // Single subfolder
        new ResourceKey("docs/readme.md").GetParent().ToString().Should().Be("project:docs");
    }

    [Test]
    public void CombineValidatesSegments()
    {
        var baseKey = new ResourceKey("folder");

        // Valid segment
        var combined = baseKey.Combine("file.txt");
        combined.ToString().Should().Be("project:folder/file.txt");

        // Empty base key
        var emptyBase = ResourceKey.Empty;
        var fromEmpty = emptyBase.Combine("file.txt");
        fromEmpty.ToString().Should().Be("project:file.txt");

        // Invalid segment with path separator throws
        var act1 = () => baseKey.Combine("sub/file.txt");
        act1.Should().Throw<ArgumentException>();

        // Null character in segment throws
        var act2 = () => baseKey.Combine("bad\0file");
        act2.Should().Throw<ArgumentException>();

        // Empty segment throws
        var act3 = () => baseKey.Combine("");
        act3.Should().Throw<ArgumentException>();
    }

    [Test]
    public void CombinePathAppendsMultipleSegments()
    {
        var baseKey = new ResourceKey("packages/king-fury");

        // A multi-segment entry path (the king-fury install regression) combines
        // segment by segment rather than being rejected as one bad segment.
        baseKey.CombinePath("audio/music.mp3").ToString()
            .Should().Be("project:packages/king-fury/audio/music.mp3");

        // A single segment behaves like Combine.
        baseKey.CombinePath("index.html").ToString()
            .Should().Be("project:packages/king-fury/index.html");

        // The root is preserved, and a root-only base key works.
        new ResourceKey("temp:").CombinePath("a/b/c.txt").ToString()
            .Should().Be("temp:a/b/c.txt");

        // An invalid segment anywhere in the path is rejected, as Combine rejects it.
        var act1 = () => baseKey.CombinePath("audio/bad\0file");
        act1.Should().Throw<ArgumentException>();

        // An empty path throws.
        var act2 = () => baseKey.CombinePath("");
        act2.Should().Throw<ArgumentException>();
    }

    [Test]
    public void EmptyKeyIsValid()
    {
        var emptyKey = ResourceKey.Empty;
        emptyKey.IsEmpty.Should().BeTrue();
        // Empty key still carries its (default) root prefix in canonical form;
        // use IsEmpty to detect the "no path" case.
        emptyKey.ToString().Should().Be("project:");
    }

    [Test]
    public void ImplicitProjectRootRoundTripsCleanly()
    {
        // Regression guard: ResourceKey "project:foo" round-trips through the implicit
        // string operator without throwing. Today's pre-redesign IsValidKey rejected the
        // ':' character via Path.GetInvalidFileNameChars() on Windows.
        ResourceKey rk = "project:foo";
        rk.Root.Should().Be("project");
        rk.Path.Should().Be("foo");
        rk.FullKey.Should().Be("project:foo");
        rk.ToString().Should().Be("project:foo");
    }

    [Test]
    public void RootAccessorReturnsParsedOrDefaultRoot()
    {
        new ResourceKey("foo/bar").Root.Should().Be("project");
        new ResourceKey("project:foo/bar").Root.Should().Be("project");
        new ResourceKey("temp:staging/foo").Root.Should().Be("temp");
        new ResourceKey("logs:session.log").Root.Should().Be("logs");
        ResourceKey.Empty.Root.Should().Be("project");
    }

    [Test]
    public void PathAccessorReturnsPathPortionOnly()
    {
        new ResourceKey("foo/bar").Path.Should().Be("foo/bar");
        new ResourceKey("project:foo/bar").Path.Should().Be("foo/bar");
        new ResourceKey("temp:staging/foo").Path.Should().Be("staging/foo");
        new ResourceKey("temp:").Path.Should().Be("");
        ResourceKey.Empty.Path.Should().Be("");
    }

    [Test]
    public void FullKeyAlwaysCarriesRootPrefix()
    {
        new ResourceKey("foo/bar").FullKey.Should().Be("project:foo/bar");
        new ResourceKey("project:foo/bar").FullKey.Should().Be("project:foo/bar");
        new ResourceKey("temp:staging/foo").FullKey.Should().Be("temp:staging/foo");
        new ResourceKey("temp:").FullKey.Should().Be("temp:");
        ResourceKey.Empty.FullKey.Should().Be("project:");
    }

    [Test]
    public void ToStringEmitsCanonicalForm()
    {
        // ToString always carries the root prefix, including "project:" for the default
        // root, so any value surfaced through ToString matches the literal form the
        // reference scanner detects and can be copy-pasted into a tracked reference.
        new ResourceKey("foo/bar").ToString().Should().Be("project:foo/bar");
        new ResourceKey("project:foo/bar").ToString().Should().Be("project:foo/bar");
        new ResourceKey("temp:staging/foo").ToString().Should().Be("temp:staging/foo");
        new ResourceKey("temp:").ToString().Should().Be("temp:");
    }

    [Test]
    public void ImplicitAndExplicitProjectRootKeysAreEqual()
    {
        // "", "project:", and ResourceKey.Empty are equivalent forms.
        var bareEmpty = new ResourceKey("");
        var explicitProject = new ResourceKey("project:");
        bareEmpty.Should().Be(explicitProject);
        bareEmpty.Should().Be(ResourceKey.Empty);

        // "foo" and "project:foo" are equivalent forms.
        new ResourceKey("foo").Should().Be(new ResourceKey("project:foo"));
        new ResourceKey("foo/bar").GetHashCode().Should().Be(new ResourceKey("project:foo/bar").GetHashCode());
    }

    [Test]
    public void InvalidRootsAreRejected()
    {
        // Empty root
        ResourceKey.IsValidKey(":foo").Should().BeFalse();
        // Uppercase root
        ResourceKey.IsValidKey("Project:foo").Should().BeFalse();
        // Single-character root
        ResourceKey.IsValidKey("a:foo").Should().BeFalse();
        // Root with leading digit
        ResourceKey.IsValidKey("1ab:foo").Should().BeFalse();
        // Root with invalid character
        ResourceKey.IsValidKey("te-mp:foo").Should().BeFalse();

        // Valid: lowercase letter followed by [a-z0-9_]+
        ResourceKey.IsValidKey("temp:foo").Should().BeTrue();
        ResourceKey.IsValidKey("logs:foo").Should().BeTrue();
        ResourceKey.IsValidKey("a1:foo").Should().BeTrue();
        ResourceKey.IsValidKey("a_b:foo").Should().BeTrue();
    }

    [Test]
    public void CombineAndGetParentPreserveRoot()
    {
        var temp = new ResourceKey("temp:staging");
        var combined = temp.Combine("file.txt");
        combined.Root.Should().Be("temp");
        combined.Path.Should().Be("staging/file.txt");
        combined.ToString().Should().Be("temp:staging/file.txt");

        var parent = combined.GetParent();
        parent.Root.Should().Be("temp");
        parent.Path.Should().Be("staging");
        parent.ToString().Should().Be("temp:staging");
    }

    [Test]
    public void IsDescendantOfRequiresSameRoot()
    {
        var tempFile = new ResourceKey("temp:staging/file.txt");
        tempFile.IsDescendantOf(new ResourceKey("temp:staging")).Should().BeTrue();

        // Different roots are never in a descendant relationship.
        tempFile.IsDescendantOf(new ResourceKey("staging")).Should().BeFalse();
        tempFile.IsDescendantOf(new ResourceKey("logs:staging")).Should().BeFalse();

        // Project-root parent of project-root child still works.
        new ResourceKey("foo/bar").IsDescendantOf(new ResourceKey("foo")).Should().BeTrue();
        new ResourceKey("project:foo/bar").IsDescendantOf(new ResourceKey("foo")).Should().BeTrue();
    }
}
