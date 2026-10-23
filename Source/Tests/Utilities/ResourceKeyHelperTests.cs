using Celbridge.Utilities;

namespace Celbridge.Tests.Utilities;

/// <summary>
/// Covers finding a resource's new key after a move.
/// </summary>
[TestFixture]
public class ResourceKeyHelperTests
{
    [Test]
    public void TryMapKey_TheMovedResource_GetsTheDestination()
    {
        var mapped = ResourceKeyHelper.TryMapKey(
            new ResourceKey("docs/intro.md"),
            new ResourceKey("docs/intro.md"),
            new ResourceKey("archive/intro.md"),
            out var movedResource);

        mapped.Should().BeTrue();
        movedResource.Should().Be(new ResourceKey("archive/intro.md"));
    }

    [Test]
    public void TryMapKey_AnItemInsideAMovedFolder_KeepsItsPathBelowTheDestination()
    {
        var mapped = ResourceKeyHelper.TryMapKey(
            new ResourceKey("docs/guides/intro.md"),
            new ResourceKey("docs"),
            new ResourceKey("archive"),
            out var movedResource);

        mapped.Should().BeTrue();
        movedResource.Should().Be(new ResourceKey("archive/guides/intro.md"));
    }

    [Test]
    public void TryMapKey_ASiblingWithTheSamePrefix_IsOutsideTheMove()
    {
        var mapped = ResourceKeyHelper.TryMapKey(
            new ResourceKey("docs-old/intro.md"),
            new ResourceKey("docs"),
            new ResourceKey("archive"),
            out _);

        mapped.Should().BeFalse();
    }

    [Test]
    public void TryMapKey_AKeyInAnotherRoot_IsOutsideTheMove()
    {
        var mapped = ResourceKeyHelper.TryMapKey(
            new ResourceKey("temp:docs/intro.md"),
            new ResourceKey("docs"),
            new ResourceKey("archive"),
            out _);

        mapped.Should().BeFalse();
    }
}
