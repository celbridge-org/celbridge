namespace Celbridge.Resources;

/// <summary>
/// Result of a single pairing pass: the classification report and the
/// sidecar-to-parent lookup.
/// </summary>
public sealed record SidecarPairingResult(
    SidecarReport Report,
    IReadOnlyDictionary<ResourceKey, ResourceKey> SidecarToParent);

/// <summary>
/// Classifies every .cel-shaped file in the project tree as a healthy sidecar,
/// a broken sidecar, or a parentless orphan.
/// </summary>
public interface ISidecarPairingService
{
    /// <summary>
    /// Walks the project root, sets each parent file's Sidecar property in place,
    /// and returns the classification report and sidecar-to-parent lookup.
    /// </summary>
    SidecarPairingResult ComputePairings(IFolderResource projectRoot, IRootHandlerRegistry rootHandlerRegistry);
}
