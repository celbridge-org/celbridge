using Celbridge.Resources.Helpers;

namespace Celbridge.Resources.Services.Roots;

/// <summary>
/// Shared implementation for resource root handlers. Owns the backing location
/// and a RootPathResolver scoped to this root; delegates both Resolve (key-to-
/// path) and GetResourceKey (path-to-key) to the resolver so both directions
/// share the same backing-location and OS-aware comparison primitives. Concrete
/// subclasses supply the root name and capability flags.
/// </summary>
public abstract class ResourceRootHandlerBase : IResourceRootHandler
{
    private readonly RootPathResolver _pathResolver;
    private readonly string _backingLocation;

    protected ResourceRootHandlerBase(string backingLocation)
    {
        _backingLocation = backingLocation;
        _pathResolver = new RootPathResolver(RootName, backingLocation, Capabilities.ListingPolicy);
    }

    public abstract string RootName { get; }

    /// <summary>
    /// Returns the listing policy for a root backed by a local folder. Linux folders compare names exactly, so
    /// they need no listings. Windows and macOS volumes ignore case, so a root there uses the policy given.
    /// </summary>
    protected static ListingPolicy GetLocalListingPolicy(ListingPolicy policy)
    {
        if (!PathComparison.IsCaseInsensitiveFileSystem)
        {
            return ListingPolicy.None;
        }

        return policy;
    }

    public string BackingLocation => _backingLocation;

    public abstract ResourceRootCapabilities Capabilities { get; }

    public Result<string> Resolve(ResourceKey key)
    {
        return _pathResolver.ValidateAndResolve(key);
    }

    /// <summary>
    /// Returns a resolve for the keys of one batch of changes. Create it after the batch's changes have been recorded
    /// with this root. The resolve accepts any folder listing started after its creation, even a stale one, so each
    /// folder is listed at most once for the batch.
    /// </summary>
    internal Func<ResourceKey, Result<string>> CreateBatchResolver()
    {
        var acceptListingsAfter = _pathResolver.GetListingTimestamp();
        return key => _pathResolver.ValidateAndResolve(key, acceptListingsAfter);
    }

    public Result<ResourceKey> GetResourceKey(string absolutePath)
    {
        return _pathResolver.GetResourceKey(absolutePath);
    }

    public Func<string, bool> PathValidator => _pathResolver.IsPathSafe;

    public void InvalidatePathCache()
    {
        _pathResolver.InvalidateCache();
    }

    public void RecordItemCreated(string absolutePath)
    {
        _pathResolver.RecordItemCreated(absolutePath);
    }

    public void RecordItemRemoved(string absolutePath)
    {
        _pathResolver.RecordItemRemoved(absolutePath);
    }

    public void RecordNameChanged(string absolutePath)
    {
        _pathResolver.RecordNameChanged(absolutePath);
    }

    /// <summary>
    /// Returns the number of folder listings this root's resolver has read.
    /// </summary>
    internal int ListingCount => _pathResolver.ListingCount;
}
