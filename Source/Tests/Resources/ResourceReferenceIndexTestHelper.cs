using Celbridge.Resources;

namespace Celbridge.Tests.Resources;

/// <summary>
/// Builds IResourceReferenceIndex stubs for tests that substitute IResourceScanner.
/// </summary>
internal static class ResourceReferenceIndexTestHelper
{
    public static IResourceReferenceIndex Empty()
    {
        var index = Substitute.For<IResourceReferenceIndex>();
        index.ReferencedTargets.Returns(Array.Empty<ResourceKey>());
        index.GetReferencers(Arg.Any<ResourceKey>()).Returns(Array.Empty<ResourceKey>());

        return index;
    }

    public static IResourceReferenceIndex WithReferencers(ResourceKey target, params ResourceKey[] referencers)
    {
        var referencedTargets = new List<ResourceKey>
        {
            target,
        };

        var index = Empty();
        index.ReferencedTargets.Returns(referencedTargets);
        index.GetReferencers(target).Returns(referencers);

        return index;
    }
}
