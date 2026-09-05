using Celbridge.Commands;
using Celbridge.Logging;

namespace Celbridge.WorkspaceUI.Services;

/// <summary>
/// Flushes the workspace items that are due to be saved. The open documents and the utilities buffer their
/// edits the same way, so one pass over both applies one save policy to them.
/// </summary>
public class WorkspaceItemSaver
{
    private readonly ILogger<WorkspaceItemSaver> _logger;
    private readonly ICommandService _commandService;
    private readonly IMessengerService _messengerService;

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
    /// Ticks each item's save timer, writes the ones that are due, and reports how many are still waiting
    /// for theirs. Delta time is the time since this method was last called.
    /// </summary>
    public async Task<Result<int>> SaveModifiedItemsAsync(
        IReadOnlyList<ISaveableWorkspaceItem> items,
        double deltaTime)
    {
        int savedCount = 0;
        int pendingSaveCount = 0;
        List<FailedResource> failedSaves = new();
        bool updateResourcesRequired = false;

        foreach (var item in items)
        {
            if (!item.HasUnsavedChanges)
            {
                continue;
            }

            var updateResult = item.UpdateSaveTimer(deltaTime);
            Guard.IsTrue(updateResult.IsSuccess); // Should never fail

            var shouldSave = updateResult.Value;
            if (!shouldSave)
            {
                pendingSaveCount++;
                continue;
            }

            var saveResult = await item.SaveAsync();
            if (saveResult.IsSuccess)
            {
                savedCount++;
                continue;
            }

            // A save failure against an item whose cached state is not Writable is the expected outcome of
            // the read-only gate in LocalResourceFileSystem. Log it for diagnostics but do not notify,
            // otherwise every auto-save tick on a locked file with buffered changes would spam the user.
            if (item.WritableState != WritableState.Writable)
            {
                _logger.LogDebug($"Skipped save for non-writable workspace item: '{item.FileResource}'");
                continue;
            }

            // MessageChain is the outer-first reason, which the toast cuts to its first line.
            failedSaves.Add(new FailedResource(item.FileResource, saveResult.MessageChain));

            // A failed save against a cache that still reads Writable suggests an external attribute flip
            // slipped past the watcher. Schedule a resource update so the cache catches up.
            updateResourcesRequired = true;
        }

        if (updateResourcesRequired)
        {
            // Debounced inside the resource service so a burst of failures from many open files collapses
            // into one project-tree rebuild.
            _commandService.Execute<IUpdateResourcesCommand>();
        }

        if (failedSaves.Count > 0)
        {
            var failedResourceNames = failedSaves.Select(failedSave => failedSave.Resource.ToString());
            var errorMessage = $"Failed to save the following workspace items: {string.Join(", ", failedResourceNames)}";
            _logger.LogError(errorMessage);

            var saveFailedMessage = new WorkspaceItemSaveFailedMessage(failedSaves);
            _messengerService.Send(saveFailedMessage);

            return Result<int>.Fail(errorMessage);
        }

        if (savedCount > 0)
        {
            _logger.LogDebug($"Saved {savedCount} modified workspace items");
        }

        return pendingSaveCount;
    }
}
