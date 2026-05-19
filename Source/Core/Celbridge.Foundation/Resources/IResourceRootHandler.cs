namespace Celbridge.Resources;

/// <summary>
/// Capability flags describing how a resource root behaves at runtime. Consulted by
/// the file system layer for write gating and by the resource monitor for watcher setup.
/// </summary>
public record ResourceRootCapabilities(
    bool IsWritable,
    bool IsWatched);

/// <summary>
/// Resolves a resource key to an absolute filesystem path under a registered root.
/// Each handler declares its root name, its backing storage location, and the
/// capabilities consumers can expect.
/// </summary>
public interface IResourceRootHandler
{
    /// <summary>
    /// The root name handled by this instance (e.g. "project", "temp", "logs").
    /// </summary>
    string RootName { get; }

    /// <summary>
    /// The absolute filesystem path that backs this root.
    /// </summary>
    string BackingLocation { get; }

    /// <summary>
    /// The capabilities of this root.
    /// </summary>
    ResourceRootCapabilities Capabilities { get; }

    /// <summary>
    /// Resolves a resource key to its absolute filesystem path under this root.
    /// Validates path containment and checks for symlinks/junctions.
    /// Fails if the path escapes the backing location or traverses a reparse point.
    /// </summary>
    Result<string> Resolve(ResourceKey key);

    /// <summary>
    /// Builds the resource key that addresses an absolute filesystem path under this root.
    /// Fails if the path is not under the backing location, or if the relative form
    /// produces an invalid key segment.
    /// </summary>
    Result<ResourceKey> GetResourceKey(string absolutePath);
}
