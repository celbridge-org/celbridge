using Celbridge.Resources;
using Celbridge.Resources.Services;

namespace Celbridge.Tests.Resources;

/// <summary>
/// A real ResourceClassifier that keeps the report from its latest pass, so a test can check the report that a
/// registry update produced.
/// </summary>
internal sealed class RecordingResourceClassifier : IResourceClassifier
{
    private readonly ResourceClassifier _classifier = ResourceClassifierTestHelper.BuildClassifier();

    public SidecarReport? LatestReport { get; private set; }

    public SidecarReport ClassifyResources(IFolderResource projectRoot, IRootHandlerRegistry rootHandlerRegistry)
    {
        var report = _classifier.ClassifyResources(projectRoot, rootHandlerRegistry);
        LatestReport = report;

        return report;
    }
}

/// <summary>
/// Helpers for tests that need a real ResourceClassifier — typically tests
/// that exercise code paths reading the .cel report or per-file Sidecar
/// pairing through the resource registry.
/// </summary>
internal static class ResourceClassifierTestHelper
{
    /// <summary>
    /// Builds a stub that returns an empty classification result on every call.
    /// Use for tests that exercise the registry but do not care about file
    /// classification (most ResourceRegistry tests).
    /// </summary>
    public static IResourceClassifier BuildEmptyStub()
    {
        var stub = Substitute.For<IResourceClassifier>();
        var emptyReport = new SidecarReport(
            Healthy: Array.Empty<ResourceKey>(),
            Broken: Array.Empty<ResourceKey>(),
            Orphan: Array.Empty<ResourceKey>());
        stub.ClassifyResources(Arg.Any<IFolderResource>(), Arg.Any<IRootHandlerRegistry>())
            .Returns(emptyReport);
        return stub;
    }

    /// <summary>
    /// Builds a real ResourceClassifier with no test-time configuration.
    /// </summary>
    public static ResourceClassifier BuildClassifier()
    {
        return new ResourceClassifier(Substitute.For<ILogger<ResourceClassifier>>());
    }
}
