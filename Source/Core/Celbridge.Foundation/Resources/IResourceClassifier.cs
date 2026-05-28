namespace Celbridge.Resources;

/// <summary>
/// Result of a single classification pass: the .cel parse-state report and
/// the sidecar-to-parent lookup.
/// </summary>
public sealed record ResourceClassificationResult(
    CelFileReport Report,
    IReadOnlyDictionary<ResourceKey, ResourceKey> SidecarToParent);

/// <summary>
/// Classifies every file in the project tree. Stamps each FileResource with
/// its FileKind (PlainData, Sidecar, Standalone, Orphan, or InvalidSidecar),
/// sets each parent file's Sidecar link in place, and produces a report
/// partitioning .cel files by parse state and orphan-ness.
/// </summary>
public interface IResourceClassifier
{
    /// <summary>
    /// Walks the project root, stamps FileKind and Sidecar on every visited
    /// FileResource, and returns the .cel parse-state report and
    /// sidecar-to-parent lookup.
    /// </summary>
    ResourceClassificationResult ClassifyResources(IFolderResource projectRoot, IRootHandlerRegistry rootHandlerRegistry);
}
