using Celbridge.Commands;
using Celbridge.Logging;

namespace Celbridge.WorkspaceUI.Services;

/// <summary>
/// What is known about a resource whose last save failed. Delay is the full wait before the next attempt,
/// Remaining is how much of that wait is left.
/// </summary>
internal sealed record SaveFailure(double Delay, double Remaining, string Reason, bool IsReported);

/// <summary>
/// Flushes the workspace items that are due to be saved.
/// </summary>
public class WorkspaceItemSaver
{
    private readonly ILogger<WorkspaceItemSaver> _logger;
    private readonly ICommandService _commandService;
    private readonly IMessengerService _messengerService;

    // Every resource whose last save failed. A non-writable resource is held here so that it backs off
    // like any other, but it is not reported.
    private readonly Dictionary<ResourceKey, SaveFailure> _saveFailures = new();

    private bool _reportedFailuresChanged;

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
    /// The resources that cannot be written.
    /// </summary>
    public IReadOnlyList<ResourceKey> GetFailingResources()
    {
        return CollectReportedFailures();
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
        AdvanceRetryWaits(deltaTime);

        int savedCount = 0;
        int pendingSaveCount = 0;
        List<ResourceKey> newlyReportedResources = new();
        bool updateResourcesRequired = false;

        foreach (var item in items)
        {
            if (!item.HasUnsavedChanges)
            {
                ForgetFailure(item.FileResource);
                continue;
            }

            var updateResult = item.UpdateSaveTimer(deltaTime);
            Guard.IsTrue(updateResult.IsSuccess); // Should never fail

            var shouldSave = updateResult.Value;
            if (!shouldSave)
            {
                // An item that is only waiting for its timer is counted as saving. One whose last attempt
                // failed is reported as failing instead.
                if (!_saveFailures.ContainsKey(item.FileResource))
                {
                    pendingSaveCount++;
                }

                continue;
            }

            if (IsWaitingToRetry(item.FileResource))
            {
                continue;
            }

            var saveResult = await SaveItemAsync(item);
            if (saveResult.IsSuccess)
            {
                savedCount++;
                ForgetFailure(item.FileResource);
                continue;
            }

            var reasonChanged = ScheduleRetry(item.FileResource, saveResult.MessageChain);

            // A non-writable item failing to save is expected, so the failure is not reported to the user.
            if (item.WritableState != WritableState.Writable)
            {
                StopReportingFailure(item.FileResource);
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

            if (ReportFailure(item.FileResource))
            {
                newlyReportedResources.Add(item.FileResource);
            }
        }

        DropStateForClosedItems(items);

        var pendingSaveMessage = new PendingSaveCountMessage(pendingSaveCount);
        _messengerService.Send(pendingSaveMessage);

        SendReportedFailuresIfChanged();

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

    // Starts reporting a resource to the user. Returns false when it is already reported.
    private bool ReportFailure(ResourceKey resource)
    {
        var failure = _saveFailures[resource];
        if (failure.IsReported)
        {
            return false;
        }

        _saveFailures[resource] = failure with { IsReported = true };
        _reportedFailuresChanged = true;

        return true;
    }

    // Stops reporting a resource to the user, leaving its wait in place so it goes on backing off.
    private void StopReportingFailure(ResourceKey resource)
    {
        if (!_saveFailures.TryGetValue(resource, out var failure) ||
            !failure.IsReported)
        {
            return;
        }

        _saveFailures[resource] = failure with { IsReported = false };
        _reportedFailuresChanged = true;
    }

    // Drops everything held about a resource, so a later failure is reported and backs off afresh.
    private void ForgetFailure(ResourceKey resource)
    {
        if (_saveFailures.Remove(resource, out var failure) &&
            failure.IsReported)
        {
            _reportedFailuresChanged = true;
        }
    }

    // Sends the reported resources whenever that set gains or loses one.
    private void SendReportedFailuresIfChanged()
    {
        if (!_reportedFailuresChanged)
        {
            return;
        }

        _reportedFailuresChanged = false;

        var failuresChangedMessage = new WorkspaceItemSaveFailuresChangedMessage(CollectReportedFailures());
        _messengerService.Send(failuresChangedMessage);
    }

    private List<ResourceKey> CollectReportedFailures()
    {
        var reportedResources = new List<ResourceKey>();

        foreach (var saveFailure in _saveFailures)
        {
            if (saveFailure.Value.IsReported)
            {
                reportedResources.Add(saveFailure.Key);
            }
        }

        return reportedResources;
    }

    // Counts down the wait before each resource whose save failed is attempted again.
    private void AdvanceRetryWaits(double deltaTime)
    {
        if (_saveFailures.Count == 0)
        {
            return;
        }

        foreach (var resource in _saveFailures.Keys.ToList())
        {
            var failure = _saveFailures[resource];
            _saveFailures[resource] = failure with { Remaining = failure.Remaining - deltaTime };
        }
    }

    // Whether the resource is still waiting out the backoff from its last failed save.
    private bool IsWaitingToRetry(ResourceKey resource)
    {
        if (!_saveFailures.TryGetValue(resource, out var failure))
        {
            return false;
        }

        return failure.Remaining > 0;
    }

    // Starts the next wait for a resource whose save failed, doubling the previous wait up to the maximum.
    // Returns true when this attempt failed for a different reason than the one before it.
    private bool ScheduleRetry(ResourceKey resource, string reason)
    {
        var delay = SaveConstants.InitialRetryDelay;
        var reasonChanged = true;
        var isReported = false;

        if (_saveFailures.TryGetValue(resource, out var previousFailure))
        {
            delay = Math.Min(previousFailure.Delay * 2, SaveConstants.MaximumRetryDelay);
            reasonChanged = previousFailure.Reason != reason;
            isReported = previousFailure.IsReported;
        }

        _saveFailures[resource] = new SaveFailure(delay, delay, reason, isReported);

        return reasonChanged;
    }

    // Forgets the resources that are no longer open, so one that fails again after being reopened is
    // reported again.
    private void DropStateForClosedItems(IReadOnlyList<IWorkspaceItem> items)
    {
        if (_saveFailures.Count == 0)
        {
            return;
        }

        var openResources = new HashSet<ResourceKey>();
        foreach (var item in items)
        {
            openResources.Add(item.FileResource);
        }

        foreach (var resource in _saveFailures.Keys.ToList())
        {
            if (!openResources.Contains(resource))
            {
                ForgetFailure(resource);
            }
        }
    }
}
