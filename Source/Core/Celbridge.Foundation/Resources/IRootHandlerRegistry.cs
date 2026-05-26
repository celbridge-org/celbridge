namespace Celbridge.Resources;

/// <summary>
/// Cross-root dispatch over a registered set of resource root handlers. Owns
/// the project, temp, and logs roots once they have been registered; resolves
/// resource keys to absolute paths and vice versa via longest-prefix-wins
/// matching across the backing locations.
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
    /// registered handler. Does not enforce case-canonical matching against
    /// the project tree; callers that want that should go through
    /// IResourceRegistry.ResolveResourcePath instead.
    /// </summary>
    Result<string> ResolveResourcePath(ResourceKey resource);

    /// <summary>
    /// Clears the path-validator cache shared by registered handlers. Call
    /// after the project folder layout changes so stale verified-folder
    /// entries do not mask new reparse-point risks.
    /// </summary>
    void InvalidatePathCache();
}
