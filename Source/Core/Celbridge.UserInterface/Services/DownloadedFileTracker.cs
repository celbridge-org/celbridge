using Celbridge.Logging;
using Celbridge.Workspace;

namespace Celbridge.UserInterface.Services;

/// <summary>
/// Follows a set of downloaded files through the resource monitor. Reports a file as missing when a change or a
/// resync finds something other than a file at its key.
/// </summary>
internal sealed class DownloadedFileTracker
{
    private readonly ILogger _logger;
    private readonly IWorkspaceWrapper _workspaceWrapper;
    private readonly Action<IReadOnlySet<ResourceKey>> _onFilesMissing;

    // Guards the subscription and the followed files, since Follow is called on several threads.
    private readonly Lock _lock = new();
    private IResourceKeySubscription? _subscription;
    private HashSet<ResourceKey> _followedFiles = new();

    public DownloadedFileTracker(
        ILogger logger,
        IWorkspaceWrapper workspaceWrapper,
        Action<IReadOnlySet<ResourceKey>> onFilesMissing)
    {
        _logger = logger;
        _workspaceWrapper = workspaceWrapper;
        _onFilesMissing = onFilesMissing;
    }

    /// <summary>
    /// Follows exactly the files given. The subscription is replaced only when the files differ from the
    /// files already followed.
    /// </summary>
    public void Follow(IReadOnlyCollection<ResourceKey> files)
    {
        lock (_lock)
        {
            if (_followedFiles.SetEquals(files))
            {
                return;
            }

            // The new subscription is made before the old one is disposed, so no change is missed between the two.
            IResourceKeySubscription? subscription = null;
            if (files.Count > 0
                && _workspaceWrapper.HasWorkspaceService)
            {
                var resourceMonitor = _workspaceWrapper.WorkspaceService.ResourceService.Monitor;
                var handlers = new ResourceChangeHandlers(OnChanges, OnResync);

                var subscribeResult = resourceMonitor.SubscribeToResources(files.ToList(), handlers);
                if (subscribeResult.IsFailure)
                {
                    _logger.LogWarning(subscribeResult, "Failed to follow the files of the finished downloads");
                    return;
                }

                subscription = subscribeResult.Value;
            }

            var previousSubscription = _subscription;
            _subscription = subscription;
            _followedFiles = files.ToHashSet();
            previousSubscription?.Dispose();
        }
    }

    /// <summary>
    /// Stops following every file.
    /// </summary>
    public void Stop()
    {
        Follow([]);
    }

    // A file is missing when something other than a file is now at its key.
    private void OnChanges(ResourceChangeBatch batch)
    {
        var missingFiles = batch.Changes
            .Where(change => change.State != ResourceItemState.File)
            .Select(change => change.Resource)
            .ToHashSet();

        ReportMissingFiles(missingFiles);
    }

    // The watcher lost events, so each followed file is checked on disk.
    private void OnResync(ResourceResync resync)
    {
        _ = CheckFilesOnDiskAsync();
    }

    private async Task CheckFilesOnDiskAsync()
    {
        if (!_workspaceWrapper.HasWorkspaceService)
        {
            return;
        }

        List<ResourceKey> followedFiles;
        lock (_lock)
        {
            followedFiles = _followedFiles.ToList();
        }

        var resourceFileSystem = _workspaceWrapper.WorkspaceService.ResourceService.FileSystem;
        var missingFiles = new HashSet<ResourceKey>();
        foreach (var followedFile in followedFiles)
        {
            var infoResult = await resourceFileSystem.GetInfoAsync(followedFile);
            if (infoResult.IsSuccess
                && infoResult.Value.Kind != StorageItemKind.File)
            {
                missingFiles.Add(followedFile);
            }
        }

        ReportMissingFiles(missingFiles);
    }

    private void ReportMissingFiles(IReadOnlySet<ResourceKey> missingFiles)
    {
        if (missingFiles.Count == 0)
        {
            return;
        }

        _onFilesMissing(missingFiles);
    }
}
