namespace Celbridge.Resources;

/// <summary>
/// Maps resource keys to absolute paths and back by dispatching to the registered
/// resource root handlers.
/// </summary>
public interface IRootHandlerRegistry
{
    /// <summary>
    /// Registers a handler for the named root. Replaces any handler previously
    /// registered against the same root name.
    /// </summary>
    void RegisterRootHandler(IResourceRootHandler handler);

    /// <summary>
    /// The currently registered root handlers, keyed by root name.
    /// </summary>
    IReadOnlyDictionary<string, IResourceRootHandler> RootHandlers { get; }

    /// <summary>
    /// Returns true if the key's root is registered. Use this for early
    /// validation at trust boundaries without performing a full resolve.
    /// </summary>
    bool IsResolvable(ResourceKey key);

    /// <summary>
    /// Maps an absolute path to its resource key by dispatching to the root
    /// handler whose backing location is the longest prefix of the path.
    /// </summary>
    Result<ResourceKey> GetResourceKey(string absolutePath);

    /// <summary>
    /// Resolves a resource key to its absolute filesystem path via the
    /// registered handler. When the root keeps listings, fails a key that does not
    /// match the name stored on disk for an existing item. The failure carries
    /// a StoredNameMismatchError that gives the stored name.
    /// </summary>
    Result<string> ResolveResourcePath(ResourceKey resource);

    /// <summary>
    /// Clears the path-validator cache on every registered handler. See
    /// IResourceRootHandler.InvalidatePathCache for when that is warranted.
    /// </summary>
    void InvalidatePathCache();
}
