using Celbridge.Resources.Helpers;

namespace Celbridge.Resources.Services;

public sealed class RootHandlerRegistry : IRootHandlerRegistry
{
    private readonly Dictionary<string, IResourceRootHandler> _rootHandlers = new(StringComparer.Ordinal);

    public void RegisterRootHandler(IResourceRootHandler handler)
    {
        _rootHandlers[handler.RootName] = handler;
    }

    public IReadOnlyDictionary<string, IResourceRootHandler> RootHandlers => _rootHandlers;

    public bool IsResolvable(ResourceKey key)
    {
        return _rootHandlers.ContainsKey(key.Root);
    }

    public Result<ResourceKey> GetResourceKey(string absolutePath)
    {
        try
        {
            // Longest-prefix-wins so a path under .celbridge/temp/ matches the
            // temp handler rather than the project handler, which has the
            // shorter project root prefix.
            var normalizedPath = Path.GetFullPath(absolutePath);

            var comparison = PathComparison.Comparison;

            IResourceRootHandler? bestHandler = null;
            int bestPrefixLength = -1;

            foreach (var handler in _rootHandlers.Values)
            {
                var backing = Path.GetFullPath(handler.BackingLocation)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                bool isBackingRoot = normalizedPath.Equals(backing, comparison);
                bool isUnderBacking = normalizedPath.StartsWith(
                    backing + Path.DirectorySeparatorChar, comparison);

                if ((isBackingRoot || isUnderBacking)
                    && backing.Length > bestPrefixLength)
                {
                    bestHandler = handler;
                    bestPrefixLength = backing.Length;
                }
            }

            if (bestHandler is null)
            {
                return Result<ResourceKey>.Fail(
                    $"The path '{absolutePath}' is not under any registered resource root.");
            }

            return bestHandler.GetResourceKey(normalizedPath);
        }
        catch (Exception ex)
        {
            return Result<ResourceKey>.Fail($"An exception occurred when getting the resource key.")
                .WithException(ex);
        }
    }

    public Result<string> ResolveResourcePath(ResourceKey resource)
    {
        if (!_rootHandlers.TryGetValue(resource.Root, out var handler))
        {
            return Result<string>.Fail(
                $"Resource root '{resource.Root}' is not registered.");
        }

        return handler.Resolve(resource);
    }

    public void InvalidatePathCache()
    {
        foreach (var handler in _rootHandlers.Values)
        {
            handler.InvalidatePathCache();
        }
    }
}
