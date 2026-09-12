namespace Celbridge.Resources.Services;

/// <summary>
/// Computes the writable state for a resource from its root handler and on-disk
/// attributes. Priority is ReadOnlyRoot > ReadOnlyAttribute — a root that holds
/// nothing writable settles the question before the file is consulted.
/// </summary>
public static class WritableStatePriority
{
    /// <summary>
    /// Evaluates the two writable-state sources in priority order and returns
    /// the first that fires, or Writable when neither does.
    /// </summary>
    public static WritableState Compute(
        ResourceKey resource,
        FileSystemAttributes attributes,
        IRootHandlerRegistry rootHandlerRegistry)
    {
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
