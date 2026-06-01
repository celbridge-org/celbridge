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
        _pathResolver = new RootPathResolver(RootName, backingLocation);
    }

    public abstract string RootName { get; }

    public string BackingLocation => _backingLocation;

    public abstract ResourceRootCapabilities Capabilities { get; }

    public Result<string> Resolve(ResourceKey key)
    {
        return _pathResolver.ValidateAndResolve(key);
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
}
