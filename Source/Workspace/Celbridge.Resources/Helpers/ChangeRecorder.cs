namespace Celbridge.Resources.Helpers;

/// <summary>
/// Records the changes made by one of Celbridge's own operations. Each create and removal reaches the path resolver of
/// the key's root at once. The operation's next resolution then sees the change without listing the folder again.
/// Disposing the recorder reports every touched key to the resource monitor, which delivers the keys to the
/// subscriptions at once. The watcher reports the same changes later.
///
/// Create the recorder in a using declaration, so the report goes out when the operation's method returns, on every
/// path. The report then comes after any message the operation sends. A tab that received the report of a move before
/// the move's key changed message would find its old key absent, and close.
/// </summary>
internal sealed class ChangeRecorder : IDisposable
{
    private readonly IResourceService _resourceService;
    private readonly List<ResourceKey> _touchedResources = new();
    private bool _isReported;

    public ChangeRecorder(IResourceService resourceService)
    {
        _resourceService = resourceService;
    }

    /// <summary>
    /// Records that an item now exists at the key: a new file or folder, a written file, or the new key of a move.
    /// </summary>
    public void RecordCreated(ResourceKey resource, string path)
    {
        Guard.IsFalse(_isReported);

        if (_resourceService.RootHandlers.RootHandlers.TryGetValue(resource.Root, out var handler))
        {
            handler.RecordItemCreated(path);
        }

        _touchedResources.Add(resource);
    }

    /// <summary>
    /// Records that the item at the key is gone: a deleted item, or the old key of a move.
    /// </summary>
    public void RecordRemoved(ResourceKey resource, string path)
    {
        Guard.IsFalse(_isReported);

        if (_resourceService.RootHandlers.RootHandlers.TryGetValue(resource.Root, out var handler))
        {
            handler.RecordItemRemoved(path);
        }

        _touchedResources.Add(resource);
    }

    /// <summary>
    /// Records an attribute change to the item at the key.
    /// </summary>
    public void RecordChanged(ResourceKey resource)
    {
        Guard.IsFalse(_isReported);

        _touchedResources.Add(resource);
    }

    /// <summary>
    /// Reports the touched keys to the resource monitor, once. A record after the report throws.
    /// </summary>
    public void Dispose()
    {
        if (_isReported)
        {
            return;
        }
        _isReported = true;

        if (_touchedResources.Count > 0)
        {
            _resourceService.Monitor.ReportOperationChanges(_touchedResources);
        }
    }
}
