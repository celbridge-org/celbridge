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
        index.GetReferencers(Arg.Any<ResourceKey>()).Returns(Array.Empty<ResourceReferenceSite>());

        return index;
    }

    public static IResourceReferenceIndex WithReferencers(ResourceKey target, params ResourceKey[] referencers)
    {
        var referencedTargets = new List<ResourceKey>
        {
            target,
        };

        // The position is immaterial to the tests that use this; each referencer is stubbed as one
        // reference at the top of its file.
        var sites = referencers
            .Select(referencer => new ResourceReferenceSite(referencer, Line: 1, Column: 1))
            .ToArray();

        var index = Empty();
        index.ReferencedTargets.Returns(referencedTargets);
        index.GetReferencers(target).Returns(sites);

        return index;
    }
}
