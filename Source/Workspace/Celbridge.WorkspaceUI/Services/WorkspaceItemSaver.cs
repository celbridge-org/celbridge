using Celbridge.Commands;
using Celbridge.Logging;

namespace Celbridge.WorkspaceUI.Services;

/// <summary>
/// The backoff for a resource whose save failed: how long the current wait is, and how much of that wait
/// is left before the next attempt.
/// </summary>
internal sealed record SaveRetry(double Delay, double Remaining);

/// <summary>
/// Flushes the workspace items that are due to be saved.
/// </summary>
public class WorkspaceItemSaver
{
    // How long a failed save waits before it is attempted again. The wait doubles with each failure up to
    // the maximum, so an item that cannot be written settles into one attempt every fifteen seconds.
    private const double InitialRetryDelay = 1.0;
    private const double MaximumRetryDelay = 15.0;

    private readonly ILogger<WorkspaceItemSaver> _logger;
    private readonly ICommandService _commandService;
    private readonly IMessengerService _messengerService;

    // The resources that cannot be written. A non-writable resource is left out, because its editor
    // already shows it as read-only.
    private readonly HashSet<ResourceKey> _failingResources = new();

    // The wait before each resource whose save failed is attempted again. Held for non-writable resources
    // too, so a locked file backs off on the same schedule.
    private readonly Dictionary<ResourceKey, SaveRetry> _saveRetries = new();

    private bool _failingResourcesChanged;

    public WorkspaceItemSaver(
        ILogger<WorkspaceItemSaver> logger,
        ICommandService commandService,
        IMessengerService messengerService)
    {
        _logger = logger;
        _commandService = commandService;
        _messengerService = messengerService;
    }

    /// <summary>
    /// Ticks each item's save timer, writes the ones that are due, and reports the items still waiting to
    /// be written and those that cannot be. A write that fails is reported once, not again on the attempts
    /// that follow it, and those attempts back off. Delta time is the time since this method was last
    /// called.
    /// </summary>
    public async Task<Result> SaveModifiedItemsAsync(
        IReadOnlyList<IWorkspaceItem> items,
        double deltaTime)
    {
        AdvanceRetryWaits(deltaTime);

        int savedCount = 0;
        int pendingSaveCount = 0;
        List<FailedResource> newFailures = new();
        bool updateResourcesRequired = false;

        foreach (var item in items)
        {
            if (!item.HasUnsavedChanges)
            {
                // Whether a save wrote the item or a reload replaced its content, it holds nothing
                // unwritten now, so any failure it was carrying is over.
                ForgetFailure(item.FileResource);
                continue;
            }

            var updateResult = item.UpdateSaveTimer(deltaTime);
            Guard.IsTrue(updateResult.IsSuccess); // Should never fail

            var shouldSave = updateResult.Value;
            if (!shouldSave)
            {
                // An item that is only waiting for its timer is counted as saving. One whose last attempt
                // failed is reported as failing instead, so the two states never read as each other.
                if (!_saveRetries.ContainsKey(item.FileResource))
                {
                    pendingSaveCount++;
                }

                continue;
            }

            if (IsWaitingToRetry(item.FileResource))
            {
                continue;
            }

            var saveResult = await item.SaveAsync();
            if (saveResult.IsSuccess)
            {
                savedCount++;
                ForgetFailure(item.FileResource);
                continue;
            }

            ScheduleRetry(item.FileResource);

            // A non-writable item failing to save is expected, and reporting it would repeat what the
            // editor already shows by dimming itself.
            if (item.WritableState != WritableState.Writable)
            {
                _logger.LogDebug($"Skipped save for non-writable workspace item: '{item.FileResource}'");
                continue;
            }

            if (!_failingResources.Add(item.FileResource))
            {
                continue;
            }

            _failingResourcesChanged = true;

            // MessageChain is the outer-first reason. It carries developer English, so the log is where
            // it belongs.
            newFailures.Add(new FailedResource(item.FileResource, saveResult.MessageChain));

            // A failed save against a cache that still reads Writable suggests an external attribute flip
            // slipped past the watcher. The rebuild below covers the project tree, so only a resource in it
            // has anything to gain from one.
            if (item.FileResource.Root == ResourceKey.DefaultRoot)
            {
                updateResourcesRequired = true;
            }
        }

        DropStateForClosedItems(items);

        var pendingSaveMessage = new PendingSaveCountMessage(pendingSaveCount);
        _messengerService.Send(pendingSaveMessage);

        SendFailingResourcesIfChanged();

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

        if (newFailures.Count == 0)
        {
            return Result.Ok();
        }

        // The reason for each failure appears here and nowhere else.
        var failureDescriptions = newFailures.Select(newFailure => $"'{newFailure.Resource}' ({newFailure.Message})");
        var errorMessage = $"Failed to save the following workspace items: {string.Join(", ", failureDescriptions)}";
        _logger.LogError(errorMessage);

        return Result.Fail(errorMessage);
    }

    // Sends the resources that cannot be written whenever that set gains or loses one.
    private void SendFailingResourcesIfChanged()
    {
        if (!_failingResourcesChanged)
        {
            return;
        }

        _failingResourcesChanged = false;

        var failingResources = new List<ResourceKey>(_failingResources);

        var failuresChangedMessage = new WorkspaceItemSaveFailuresChangedMessage(failingResources);
        _messengerService.Send(failuresChangedMessage);
    }

    // Counts down the wait before each resource whose save failed is attempted again.
    private void AdvanceRetryWaits(double deltaTime)
    {
        if (_saveRetries.Count == 0)
        {
            return;
        }

        foreach (var resource in _saveRetries.Keys.ToList())
        {
            var retry = _saveRetries[resource];
            _saveRetries[resource] = retry with { Remaining = retry.Remaining - deltaTime };
        }
    }

    // Whether the resource is still waiting out the backoff from its last failed save.
    private bool IsWaitingToRetry(ResourceKey resource)
    {
        if (!_saveRetries.TryGetValue(resource, out var retry))
        {
            return false;
        }

        return retry.Remaining > 0;
    }

    // Starts the next wait for a resource whose save failed, doubling the previous wait up to the maximum.
    private void ScheduleRetry(ResourceKey resource)
    {
        var delay = InitialRetryDelay;
        if (_saveRetries.TryGetValue(resource, out var previousRetry))
        {
            delay = Math.Min(previousRetry.Delay * 2, MaximumRetryDelay);
        }

        _saveRetries[resource] = new SaveRetry(delay, delay);
    }

    // Forgets a resource that holds nothing unwritten, so a later failure is reported and backs off afresh.
    private void ForgetFailure(ResourceKey resource)
    {
        if (_failingResources.Remove(resource))
        {
            _failingResourcesChanged = true;
        }

        _saveRetries.Remove(resource);
    }

    // Forgets the resources that are no longer open, so one that fails again after being reopened is
    // reported again.
    private void DropStateForClosedItems(IReadOnlyList<IWorkspaceItem> items)
    {
        if (_failingResources.Count == 0 &&
            _saveRetries.Count == 0)
        {
            return;
        }

        var openResources = new HashSet<ResourceKey>();
        foreach (var item in items)
        {
            openResources.Add(item.FileResource);
        }

        var closedFailureCount = _failingResources.RemoveWhere(resource => !openResources.Contains(resource));
        if (closedFailureCount > 0)
        {
            _failingResourcesChanged = true;
        }

        foreach (var resource in _saveRetries.Keys.ToList())
        {
            if (!openResources.Contains(resource))
            {
                _saveRetries.Remove(resource);
            }
        }
    }
}
