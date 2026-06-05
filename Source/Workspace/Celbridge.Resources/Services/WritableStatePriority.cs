namespace Celbridge.Resources.Services;

/// <summary>
/// Computes the writable state for a resource from its policy verdict, root
/// handler, and on-disk attributes. Priority is Locked > ReadOnlyRoot >
/// ReadOnlyAttribute — configured locks dominate ambient state.
/// </summary>
public static class WritableStatePriority
{
    /// <summary>
    /// Evaluates the three writable-state sources in priority order and returns
    /// the first that fires, or Writable when none does.
    /// </summary>
    public static WritableState Compute(
        ResourceKey resource,
        bool isFolder,
        FileSystemAttributes attributes,
        IResourcePolicy policy,
        IRootHandlerRegistry rootHandlerRegistry)
    {
        var writeResult = policy.Evaluate(resource, ResourceAction.Write, isFolder);
        if (writeResult.IsFailure)
        {
            return WritableState.Locked;
        }

        if (rootHandlerRegistry.RootHandlers.TryGetValue(resource.Root, out var handler)
            && !handler.Capabilities.IsWritable)
        {
            return WritableState.ReadOnlyRoot;
        }

        if ((attributes & FileSystemAttributes.ReadOnly) != 0)
        {
            return WritableState.ReadOnlyAttribute;
        }

        return WritableState.Writable;
    }
}
