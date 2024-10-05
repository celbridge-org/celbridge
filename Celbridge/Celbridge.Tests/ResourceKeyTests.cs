using Celbridge.Core;

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

        ResourceKey.IsValidSegment("Invalid\"Segment").Should().BeFalse();
        ResourceKey.IsValidKey(@"C:\\AbsolutePath").Should().BeFalse();
        ResourceKey.IsValidKey(@"\AbsolutePath").Should().BeFalse();
        ResourceKey.IsValidKey(@"/Some/Path/File.txt").Should().BeFalse();
    }
}
