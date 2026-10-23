using Celbridge.Resources;

namespace Celbridge.Utilities;

/// <summary>
/// Helpers for working with resource keys.
/// </summary>
public static class ResourceKeyHelper
{
    /// <summary>
    /// Finds a resource's new key after a move from sourceResource to destResource. The moved resource itself gets
    /// destResource. An item inside a moved folder keeps its path within the folder, below destResource. Returns false
    /// for a resource outside the move.
    /// </summary>
    public static bool TryMapKey(
        ResourceKey resource,
        ResourceKey sourceResource,
        ResourceKey destResource,
        out ResourceKey movedResource)
    {
        if (resource == sourceResource)
        {
            movedResource = destResource;
            return true;
        }

        if (!sourceResource.IsEmpty
            && resource.IsDescendantOf(sourceResource))
        {
            var pathInsideFolder = resource.Path.Substring(sourceResource.Path.Length + 1);
            movedResource = destResource.CombinePath(pathInsideFolder);
            return true;
        }

        movedResource = ResourceKey.Empty;
        return false;
    }
}
