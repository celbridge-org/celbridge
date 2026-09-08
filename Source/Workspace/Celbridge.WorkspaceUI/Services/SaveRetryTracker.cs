namespace Celbridge.WorkspaceUI.Services;

/// <summary>
/// What is known about a resource that is waiting to be written again. Delay is the full wait before the
/// next attempt, Remaining is how much of that wait is left.
/// </summary>
internal sealed record SaveRetry(double Delay, double Remaining, string Reason, bool IsReported);

/// <summary>
/// The resources that are waiting to be written again, how long each one waits before the next attempt,
/// and which of them are reported to the user.
/// </summary>
public class SaveRetryTracker
{
    // A non-writable resource is held here so that it backs off like any other, but it is not reported.
    private readonly Dictionary<ResourceKey, SaveRetry> _saveRetries = new();

    /// <summary>
    /// Whether the reported resources have changed since they were last cleared.
    /// </summary>
    public bool HasReportedChanges { get; private set; }

    public void ClearReportedChanges()
    {
        HasReportedChanges = false;
    }

    /// <summary>
    /// Whether the resource is waiting to be written again.
    /// </summary>
    public bool IsRetrying(ResourceKey resource)
    {
        return _saveRetries.ContainsKey(resource);
    }

    /// <summary>
    /// Whether the resource is still waiting out the backoff from its last failed save.
    /// </summary>
    public bool IsWaiting(ResourceKey resource)
    {
        if (!_saveRetries.TryGetValue(resource, out var retry))
        {
            return false;
        }

        return retry.Remaining > 0;
    }

    /// <summary>
    /// Starts the next wait for a resource whose save failed, doubling the previous wait up to the maximum.
    /// Returns true when this attempt failed for a different reason than the one before it.
    /// </summary>
    public bool Schedule(ResourceKey resource, string reason)
    {
        var delay = SaveConstants.InitialRetryDelay;
        var reasonChanged = true;
        var isReported = false;

        if (_saveRetries.TryGetValue(resource, out var previousRetry))
        {
            delay = Math.Min(previousRetry.Delay * 2, SaveConstants.MaximumRetryDelay);
            reasonChanged = previousRetry.Reason != reason;
            isReported = previousRetry.IsReported;
        }

        _saveRetries[resource] = new SaveRetry(delay, delay, reason, isReported);

        return reasonChanged;
    }

    /// <summary>
    /// Counts down the retry timer of every resource waiting to be written again.
    /// </summary>
    public void UpdateTimers(double deltaTime)
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

    /// <summary>
    /// Starts reporting a resource to the user. Returns false when it is already reported.
    /// </summary>
    public bool StartReporting(ResourceKey resource)
    {
        var retry = _saveRetries[resource];
        if (retry.IsReported)
        {
            return false;
        }

        _saveRetries[resource] = retry with { IsReported = true };
        HasReportedChanges = true;

        return true;
    }

    /// <summary>
    /// Stops reporting a resource to the user, leaving its wait in place so it goes on backing off.
    /// </summary>
    public void StopReporting(ResourceKey resource)
    {
        if (!_saveRetries.TryGetValue(resource, out var retry) ||
            !retry.IsReported)
        {
            return;
        }

        _saveRetries[resource] = retry with { IsReported = false };
        HasReportedChanges = true;
    }

    /// <summary>
    /// Drops everything held about a resource, so a later failure is reported and backs off afresh.
    /// </summary>
    public void Forget(ResourceKey resource)
    {
        if (_saveRetries.Remove(resource, out var retry) &&
            retry.IsReported)
        {
            HasReportedChanges = true;
        }
    }

    /// <summary>
    /// Drops every resource that is not in the supplied set.
    /// </summary>
    public void ForgetAllExcept(IReadOnlyCollection<ResourceKey> resources)
    {
        if (_saveRetries.Count == 0)
        {
            return;
        }

        foreach (var resource in _saveRetries.Keys.ToList())
        {
            if (!resources.Contains(resource))
            {
                Forget(resource);
            }
        }
    }

    /// <summary>
    /// Every resource that is reported to the user.
    /// </summary>
    public IReadOnlyList<ResourceKey> GetReported()
    {
        var reportedResources = new List<ResourceKey>();

        foreach (var saveRetry in _saveRetries)
        {
            if (saveRetry.Value.IsReported)
            {
                reportedResources.Add(saveRetry.Key);
            }
        }

        return reportedResources;
    }
}
