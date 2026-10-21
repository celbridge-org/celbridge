namespace Celbridge.Resources;

/// <summary>
/// How long a root's path resolver keeps the listing of a folder. The resolver reads the listings to check that
/// a key uses each existing item's name as it is stored on disk.
/// </summary>
public enum ListingPolicy
{
    /// <summary>
    /// The root's own lookups are exact, so the resolver skips the listings and the stored name check.
    /// </summary>
    None,

    /// <summary>
    /// Every change reaches the resolver, through the watcher or through Celbridge's own operations. A listing
    /// lasts until a change drops it.
    /// </summary>
    UntilChanged,

    /// <summary>
    /// Other processes can change the root unseen. A cached folder and its listing last a minute.
    /// </summary>
    Expiring,
}

/// <summary>
/// Capability flags for a resource root. They say whether the root is writable and
/// watched, and how long the root's path resolver keeps folder listings.
/// </summary>
public record ResourceRootCapabilities(
    bool IsWritable,
    bool IsWatched,
    ListingPolicy ListingPolicy);

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
    /// When the root keeps listings, also fails a key that differs from the name
    /// stored on disk for an existing item. The names can differ in letter case or Unicode
    /// normalization. The failure carries a StoredNameMismatchError that gives the
    /// stored name.
    /// </summary>
    Result<string> Resolve(ResourceKey key);

    /// <summary>
    /// Builds the resource key that addresses an absolute filesystem path under this root.
    /// Fails if the path is not under the backing location, or if the relative form
    /// produces an invalid key segment.
    /// </summary>
    Result<ResourceKey> GetResourceKey(string absolutePath);

    /// <summary>
    /// Returns true if the absolute path is safe to address as a resource
    /// under this root. The specific safety checks are per-handler; consumers
    /// treat a false result as a filter, not a recoverable error.
    /// </summary>
    Func<string, bool> PathValidator { get; }

    /// <summary>
    /// Clears this handler's path cache so subsequent resolves verify and list folders again. Otherwise each
    /// folder is verified once, and the recorded changes keep the folder's listing current. Clearing is reserved
    /// for changes that may have gone unobserved.
    /// </summary>
    void InvalidatePathCache();

    /// <summary>
    /// Records that an item now exists at the absolute path, so the item's name joins the listing of its folder.
    /// Call it for every create, and for the new path of every move or rename.
    /// </summary>
    void RecordItemCreated(string absolutePath);

    /// <summary>
    /// Records that the item at the absolute path is gone, so the item's name leaves the listing of its folder.
    /// Every cached folder at or below the path is dropped too. Call it for every delete, and for the old path of
    /// every move or rename.
    /// </summary>
    void RecordItemRemoved(string absolutePath);
}
