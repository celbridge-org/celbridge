namespace Celbridge.Resources;

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
