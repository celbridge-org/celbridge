using Celbridge.Documents;

namespace Celbridge.WebHost.Services;

/// <summary>
/// How a hosted page's rendering process changed between two observations.
/// </summary>
internal enum HostedPageProcessChange
{
    None,
    Gone,
    Relaunched,
    Replaced
}

/// <summary>
/// Counts what the host has observed about each hosted page still working, keyed by the page. A page is
/// counted only while it is tracked, so an observation that arrives after the page is closed is discarded
/// rather than resurrecting its entry. Every member is safe to call from any thread.
/// </summary>
internal sealed class HostedPageHealthTracker<TPage>
    where TPage : class
{
    // Distinguishes a process id that has never been read from one read as absent.
    private const long UnknownProcessId = -1;

    private sealed class PageCounters
    {
        public int WakeFailures;
        public int ProcessFailures;
        public long ProcessId = UnknownProcessId;
    }

    private readonly object _lock = new();
    private readonly Dictionary<TPage, PageCounters> _pages = new();

    public void Track(TPage page)
    {
        lock (_lock)
        {
            _pages[page] = new PageCounters();
        }
    }

    public void Untrack(TPage page)
    {
        lock (_lock)
        {
            _pages.Remove(page);
        }
    }

    /// <summary>
    /// Clears the page's missed wakes and returns how many were cleared, so a caller can report a recovery.
    /// </summary>
    public int RecordWakeSucceeded(TPage page)
    {
        lock (_lock)
        {
            if (!_pages.TryGetValue(page, out var counters))
            {
                return 0;
            }

            var clearedFailures = counters.WakeFailures;
            counters.WakeFailures = 0;
            return clearedFailures;
        }
    }

    /// <summary>
    /// Counts a missed wake and returns the page's consecutive total, or zero when it is no longer tracked.
    /// </summary>
    public int RecordWakeFailed(TPage page)
    {
        lock (_lock)
        {
            if (!_pages.TryGetValue(page, out var counters))
            {
                return 0;
            }

            return ++counters.WakeFailures;
        }
    }

    /// <summary>
    /// Records the process rendering the page, counting a process failure when it goes absent or is replaced.
    /// A negative id means the platform could not report one and leaves the page's state untouched.
    /// </summary>
    public HostedPageProcessChange RecordProcessId(TPage page, long processId)
    {
        lock (_lock)
        {
            if (processId < 0
                || !_pages.TryGetValue(page, out var counters))
            {
                return HostedPageProcessChange.None;
            }

            var previousProcessId = counters.ProcessId;
            counters.ProcessId = processId;

            if (processId == 0)
            {
                // The renderer stays absent across later wakes, so its death counts once.
                if (previousProcessId <= 0)
                {
                    return HostedPageProcessChange.None;
                }

                counters.ProcessFailures++;
                return HostedPageProcessChange.Gone;
            }

            if (previousProcessId == 0)
            {
                // The death this replaces was counted when the renderer went absent.
                return HostedPageProcessChange.Relaunched;
            }

            if (previousProcessId == UnknownProcessId
                || previousProcessId == processId)
            {
                return HostedPageProcessChange.None;
            }

            counters.ProcessFailures++;
            return HostedPageProcessChange.Replaced;
        }
    }

    public DocumentHealth GetHealth(TPage page)
    {
        lock (_lock)
        {
            return _pages.TryGetValue(page, out var counters)
                ? new DocumentHealth(counters.WakeFailures, counters.ProcessFailures)
                : DocumentHealth.Healthy;
        }
    }
}
