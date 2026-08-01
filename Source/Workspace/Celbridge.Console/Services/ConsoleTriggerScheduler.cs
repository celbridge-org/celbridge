namespace Celbridge.Console.Services;

/// <summary>
/// Debounces trigger fires per distinct command, so a burst of matching resource changes runs that command
/// once, after the changes have stopped.
/// </summary>
public sealed class ConsoleTriggerScheduler
{
    // Long enough to absorb the burst a single save produces (the watcher reports one write as several
    // events), short enough that a trigger still feels immediate. Not configurable per trigger: the value
    // that would need tuning is a property of whatever wrote the file, which the user has no way to know.
    private const int DebounceMilliseconds = 300;

    private readonly Action<Guid, string> _fire;
    private readonly Func<int, Task> _delayAsync;

    private readonly object _pendingLock = new();

    // The most recent request seen for each command. A wait that wakes to find a higher number has been
    // superseded, which is what makes a fresh request restart the wait rather than run alongside it.
    private readonly Dictionary<(Guid SessionId, string Invocation), int> _pendingRequests = new();

    /// <summary>
    /// The fire callback must not throw: it runs on a background task with nothing to observe it. The delay
    /// hook exists so tests can drive the wait without a real clock.
    /// </summary>
    public ConsoleTriggerScheduler(Action<Guid, string> fire, Func<int, Task>? delayAsync = null)
    {
        _fire = fire;
        _delayAsync = delayAsync ?? (debounceMilliseconds => Task.Delay(debounceMilliseconds));
    }

    /// <summary>
    /// Requests a run of a command in a session once matching changes have been quiet for the debounce
    /// period. A repeat of the same command restarts that wait, so a resource written in several steps runs
    /// the command after the writing stops rather than part way through it.
    /// </summary>
    public void Schedule(Guid sessionId, string invocation)
    {
        var key = (sessionId, invocation);

        int requestNumber;
        lock (_pendingLock)
        {
            _pendingRequests.TryGetValue(key, out var previousRequestNumber);
            requestNumber = previousRequestNumber + 1;
            _pendingRequests[key] = requestNumber;
        }

        _ = RunAfterQuietAsync(key, requestNumber);
    }

    private async Task RunAfterQuietAsync((Guid SessionId, string Invocation) key, int requestNumber)
    {
        await _delayAsync(DebounceMilliseconds);

        lock (_pendingLock)
        {
            // A later request arrived during the wait, so that request's own wait is the one that fires.
            if (!_pendingRequests.TryGetValue(key, out var latestRequestNumber) ||
                latestRequestNumber != requestNumber)
            {
                return;
            }

            // Cleared before the command fires, so a change arriving while it runs starts a fresh wait
            // rather than being absorbed by one that is already spent.
            _pendingRequests.Remove(key);
        }

        _fire(key.SessionId, key.Invocation);
    }
}
