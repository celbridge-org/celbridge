namespace Celbridge.Resources.Helpers;

/// <summary>
/// Records the gateway's own creates and removals with the path resolver of the key's root. The next
/// resolution then sees the change without listing the folder again. The watcher records the same change
/// later.
/// </summary>
internal static class ChangeRecorder
{
    public static void RecordCreated(IRootHandlerRegistry rootHandlers, ResourceKey resource, string path)
    {
        if (rootHandlers.RootHandlers.TryGetValue(resource.Root, out var handler))
        {
            handler.RecordItemCreated(path);
        }
    }

    public static void RecordRemoved(IRootHandlerRegistry rootHandlers, ResourceKey resource, string path)
    {
        if (rootHandlers.RootHandlers.TryGetValue(resource.Root, out var handler))
        {
            handler.RecordItemRemoved(path);
        }
    }
}
