using Celbridge.Commands;
using Celbridge.Logging;

namespace Celbridge.WorkspaceUI.Services;

/// <summary>
/// Where a workspace item stands with writing its content to its file resource.
/// </summary>
internal enum SaveState
{
    /// <summary>
    /// The item holds no content that has yet to be written.
    /// </summary>
    Saved,

    /// <summary>
    /// The item holds content that is waiting for its save timer to come due.
    /// </summary>
    Pending,

    /// <summary>
    /// The item's last write failed, so it is waiting to be attempted again.
    /// </summary>
    Retrying,
}

/// <summary>
/// Flushes the workspace items that are due to be saved.
/// </summary>
public class WorkspaceItemSaver
{
    private readonly ILogger<WorkspaceItemSaver> _logger;
    private readonly ICommandService _commandService;
    private readonly IMessengerService _messengerService;
    private readonly SaveRetryTracker _retries;

    // The resources reported to the user as failing, as of the last save pass.
    private readonly HashSet<ResourceKey> _reportedResources = new();

    public WorkspaceItemSaver(
        ILogger<WorkspaceItemSaver> logger,
        ICommandService commandService,
        IMessengerService messengerService,
        SaveRetryTracker retries)
    {
        _logger = logger;
        _commandService = commandService;
        _messengerService = messengerService;
        _retries = retries;
    }

    /// <summary>
    /// Every resource whose last write failed and is waiting to be attempted again.
    /// </summary>
    public IReadOnlyList<ResourceKey> GetRetryingResources()
    {
        return _reportedResources.ToList();
    }

    /// <summary>
    /// Writes every item that still holds unsaved changes, allowing each one the timeout in seconds to
    /// write. An item that has not written by then is abandoned and its unsaved content is lost.
    /// </summary>
    public async Task FlushModifiedItemsAsync(IReadOnlyList<IWorkspaceItem> items, double timeout)
    {
        foreach (var item in items)
        {
            if (!item.HasUnsavedChanges)
            {
                continue;
            }

            var saveTask = SaveItemAsync(item);
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeout));
            var completedTask = await Task.WhenAny(saveTask, timeoutTask);

            if (completedTask != saveTask)
            {
                _logger.LogError($"Workspace item did not write within {timeout}s, so its unsaved content was discarded: '{item.FileResource}'");
                continue;
            }

            var saveResult = await saveTask;
            if (saveResult.IsFailure)
            {
                _logger.LogError($"Failed to write unsaved content, so it was discarded: '{item.FileResource}'. {saveResult.DiagnosticReport}");
            }
        }
    }

    /// <summary>
    /// Ticks each item's save timer, writes the ones that are due, and reports the items still waiting to
    /// be written and those that cannot be. A failed write is reported once and its retries back off.
    /// Delta time is the time since this method was last called.
    /// </summary>
    public async Task<Result> SaveModifiedItemsAsync(
        IReadOnlyList<IWorkspaceItem> items,
        double deltaTime)
    {
        _retries.UpdateTimers(deltaTime);

        int savedCount = 0;
        int pendingSaveCount = 0;
        bool updateResourcesRequired = false;

        foreach (var item in items)
        {
            var saveState = GetSaveState(item);
            if (saveState == SaveState.Saved)
            {
                _retries.Forget(item.FileResource);
                continue;
            }

            var updateResult = item.UpdateSaveTimer(deltaTime);
            Guard.IsTrue(updateResult.IsSuccess); // Should never fail

            var shouldSave = updateResult.Value;
            if (!shouldSave)
            {
                if (saveState == SaveState.Pending)
                {
                    pendingSaveCount++;
                }

                continue;
            }

            if (_retries.IsWaiting(item.FileResource))
            {
                continue;
            }

            var saveResult = await SaveItemAsync(item);
            if (saveResult.IsSuccess)
            {
                savedCount++;
                _retries.Forget(item.FileResource);
                continue;
            }

            var reasonChanged = _retries.Schedule(item.FileResource, saveResult.MessageChain);

            if (item.WritableState != WritableState.Writable)
            {
                _logger.LogDebug($"Skipped save for non-writable workspace item: '{item.FileResource}'");
                continue;
            }

            // A failed save against a cache that still reads Writable suggests an external attribute flip
            // slipped past the watcher. The rebuild below covers the project tree, so only a resource in it
            // has anything to gain from one.
            if (item.FileResource.Root == ResourceKey.DefaultRoot)
            {
                updateResourcesRequired = true;
            }

            // Logged only when the reason changes, so a file that goes on failing does not fill the log.
            if (reasonChanged)
            {
                _logger.LogError($"Failed to save workspace item '{item.FileResource}'. {saveResult.MessageChain}");
            }
        }

        DropStateForClosedItems(items);

        var pendingSaveMessage = new PendingSaveCountMessage(pendingSaveCount);
        _messengerService.Send(pendingSaveMessage);

        var newlyReportedResources = UpdateReportedResources(items);

        if (updateResourcesRequired)
        {
            // Debounced inside the resource service so a burst of failures from many open files collapses
            // into one project-tree rebuild.
            _commandService.Execute<IUpdateResourcesCommand>();
        }

        if (savedCount > 0)
        {
            _logger.LogDebug($"Saved {savedCount} modified workspace items");
        }

        if (newlyReportedResources.Count == 0)
        {
            return Result.Ok();
        }

        var resourceNames = newlyReportedResources.Select(resource => $"'{resource}'");

        return Result.Fail($"Failed to save the following workspace items: {string.Join(", ", resourceNames)}");
    }

    // Where the item stands with writing. A resource waiting to be written again is Retrying rather than
    // Pending, so the two never count the same item.
    private SaveState GetSaveState(IWorkspaceItem item)
    {
        if (!item.HasUnsavedChanges)
        {
            return SaveState.Saved;
        }

        if (_retries.IsRetrying(item.FileResource))
        {
            return SaveState.Retrying;
        }

        return SaveState.Pending;
    }

    // Writes the item, turning an exception into a failed result so one editor cannot stop the save pass.
    private async Task<Result> SaveItemAsync(IWorkspaceItem item)
    {
        try
        {
            return await item.SaveAsync();
        }
        catch (Exception exception)
        {
            return Result.Fail($"An exception occurred while saving workspace item: '{item.FileResource}'")
                .WithException(exception);
        }
    }

    // Sends the reported resources whenever that set gains or loses one, and returns the resources that
    // have just started being reported.
    private List<ResourceKey> UpdateReportedResources(IReadOnlyList<IWorkspaceItem> items)
    {
        var reportedResources = CollectReportedResources(items);
        if (_reportedResources.SetEquals(reportedResources))
        {
            return new List<ResourceKey>();
        }

        var newlyReportedResources = reportedResources
            .Where(resource => !_reportedResources.Contains(resource))
            .ToList();

        _reportedResources.Clear();
        _reportedResources.UnionWith(reportedResources);

        var retriesChangedMessage = new WorkspaceItemSaveRetriesChangedMessage(reportedResources);
        _messengerService.Send(retriesChangedMessage);

        return newlyReportedResources;
    }

    private List<ResourceKey> CollectReportedResources(IReadOnlyList<IWorkspaceItem> items)
    {
        var reportedResources = new List<ResourceKey>();

        foreach (var item in items)
        {
            // A non-writable item failing to save is expected, and its editor already shows it as
            // read-only, so it backs off without being reported.
            if (_retries.IsRetrying(item.FileResource) &&
                item.WritableState == WritableState.Writable)
            {
                reportedResources.Add(item.FileResource);
            }
        }

        return reportedResources;
    }

    // Forgets the resources that are no longer open, so one that fails again after being reopened is
    // reported again.
    private void DropStateForClosedItems(IReadOnlyList<IWorkspaceItem> items)
    {
        var openResources = new HashSet<ResourceKey>();
        foreach (var item in items)
        {
            openResources.Add(item.FileResource);
        }

        _retries.ForgetAllExcept(openResources);
    }
}
