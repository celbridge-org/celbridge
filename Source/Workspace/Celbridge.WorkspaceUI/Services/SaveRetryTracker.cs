namespace Celbridge.WorkspaceUI.Services;

/// <summary>
/// What is known about a resource that is waiting to be written again. Delay is the full wait before the
/// next attempt, Remaining is how much of that wait is left.
/// </summary>
internal sealed record SaveRetry(double Delay, double Remaining, string Reason);

/// <summary>
/// The resources that are waiting to be written again, and how long each one waits before the next
/// attempt.
/// </summary>
public class SaveRetryTracker
{
    private readonly Dictionary<ResourceKey, SaveRetry> _saveRetries = new();

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

        if (_saveRetries.TryGetValue(resource, out var previousRetry))
        {
            delay = Math.Min(previousRetry.Delay * 2, SaveConstants.MaximumRetryDelay);
            reasonChanged = previousRetry.Reason != reason;
        }

        _saveRetries[resource] = new SaveRetry(delay, delay, reason);

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
    /// Drops everything held about a resource, so a later failure backs off afresh.
    /// </summary>
    public void Forget(ResourceKey resource)
    {
        _saveRetries.Remove(resource);
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
}
