namespace Celbridge.Resources;

/// <summary>
/// The .cel files in the project tree, partitioned by parse state and orphan-ness. Parse state (Healthy / Broken)
/// and orphan-ness are orthogonal: an orphan .cel file with malformed content appears in both Broken and Orphan.
/// Files ending in .cel.cel are surfaced as Broken and are never treated as sidecars.
/// </summary>
public record SidecarReport(
    IReadOnlyList<ResourceKey> Healthy,
    IReadOnlyList<ResourceKey> Broken,
    IReadOnlyList<ResourceKey> Orphan);

/// <summary>
/// Classifies every file in the project tree. Stamps each FileResource with
/// its FileKind, sets each parent file's Sidecar link in place, and produces
/// a report partitioning .cel files by parse state and orphan-ness.
/// </summary>
public interface IResourceClassifier
{
    /// <summary>
    /// Walks the project root, stamps FileKind and Sidecar on every visited
    /// FileResource, and returns the .cel parse-state report.
    /// </summary>
    SidecarReport ClassifyResources(IFolderResource projectRoot, IRootHandlerRegistry rootHandlerRegistry);
}
