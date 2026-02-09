namespace Celbridge.Tests;

[TestFixture]
public class ResourceKeyTests
{
    [Test]
    public void ICanValidateResourceKeys()
    {
        //
        // Check valid paths pass
        //

        ResourceKey.IsValidSegment("ValidSegment").Should().BeTrue();
        ResourceKey.IsValidKey(@"Some/Path/File.txt").Should().BeTrue();

        //
        // Check invalid paths fail
        //

        ResourceKey.IsValidSegment("Invalid\0Segment").Should().BeFalse();
        ResourceKey.IsValidKey(@"C:\\AbsolutePath").Should().BeFalse();
        ResourceKey.IsValidKey(@"\AbsolutePath").Should().BeFalse();
        ResourceKey.IsValidKey(@"/Some/Path/File.txt").Should().BeFalse();
        ResourceKey.IsValidKey(@"Some/Path/File.txt/").Should().BeFalse(); // Trailing slash
    }

    [Test]
    public void CreateThrowsOnInvalidKey()
    {
        // Valid keys should not throw
        var validKey = ResourceKey.Create("Some/Path/File.txt");
        validKey.ToString().Should().Be("Some/Path/File.txt");

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
        validKey.ToString().Should().Be("Some/Path/File.txt");

        // Empty key is valid
        ResourceKey.TryCreate("", out var emptyKey).Should().BeTrue();
        emptyKey.IsEmpty.Should().BeTrue();

        // Invalid keys should return false
        ResourceKey.TryCreate(@"C:\AbsolutePath", out var invalidKey1).Should().BeFalse();
        invalidKey1.IsEmpty.Should().BeTrue();

        ResourceKey.TryCreate("/Some/Path/File.txt", out var invalidKey2).Should().BeFalse();
        invalidKey2.IsEmpty.Should().BeTrue();

        ResourceKey.TryCreate("Some/Path/File.txt/", out var invalidKey3).Should().BeFalse();
        invalidKey3.IsEmpty.Should().BeTrue();
    }

    [Test]
    public void IsDescendantOfHandlesTrailingSlashDefensively()
    {
        var childKey = new ResourceKey("folder/subfolder/file.txt");

        // Normal case - no trailing slash
        childKey.IsDescendantOf(new ResourceKey("folder")).Should().BeTrue();
        childKey.IsDescendantOf(new ResourceKey("folder/subfolder")).Should().BeTrue();

        // Edge case - trailing slash (invalid but handled gracefully)
        childKey.IsDescendantOf(new ResourceKey("folder/")).Should().BeTrue();
        childKey.IsDescendantOf(new ResourceKey("folder/subfolder/")).Should().BeTrue();

        // Non-descendants
        childKey.IsDescendantOf(new ResourceKey("other")).Should().BeFalse();
        childKey.IsDescendantOf(new ResourceKey("fold")).Should().BeFalse(); // Partial match
    }
}
