namespace Celbridge.WebHost.Services;

/// <summary>
/// How a hosted page's rendering process changed between two observations.
/// </summary>
internal enum PageProcessChange
{
    None,
    Gone,
    Relaunched,

    /// <summary>
    /// One running renderer gave way to another, which WebKit does to a page it has suspended in the
    /// background. The page still has a renderer, so nothing has failed.
    /// </summary>
    Replaced
}

/// <summary>
/// Counts what the host has observed about one web view's page still working. Every member is safe to call
/// from any thread.
/// </summary>
internal sealed class WebViewHealthTracker
{
    // Distinguishes a process id that has never been read from one read as absent.
    private const long UnknownProcessId = -1;

    private readonly object _lock = new();

    private int _wakeFailures;
    private int _processFailures;
    private long _processId = UnknownProcessId;
    private string _address = string.Empty;

    /// <summary>
    /// Clears the page's missed wakes and returns how many were cleared, so a caller can report a recovery.
    /// </summary>
    public int RecordWakeSucceeded()
    {
        lock (_lock)
        {
            var clearedFailures = _wakeFailures;
            _wakeFailures = 0;
            return clearedFailures;
        }
    }

    /// <summary>
    /// Counts a missed wake and returns the page's consecutive total.
    /// </summary>
    public int RecordWakeFailed()
    {
        lock (_lock)
        {
            return ++_wakeFailures;
        }
    }

    /// <summary>
    /// Counts a failure of the page's rendering process that the platform reported.
    /// </summary>
    public void RecordProcessFailed()
    {
        lock (_lock)
        {
            _processFailures++;
        }
    }

    /// <summary>
    /// Records where the page is navigating and forgets which process was rendering it, so the next reading
    /// is a fresh baseline. WebKit swaps the prewarmed process for the page's own on the first real load,
    /// and a swap the navigation asked for is not a renderer failing. The address is kept because a page
    /// whose renderer has gone can no longer report one, and that is when naming it matters most.
    /// </summary>
    public void RecordNavigation(string? address)
    {
        lock (_lock)
        {
            _processId = UnknownProcessId;
            _address = address ?? string.Empty;
        }
    }

    /// <summary>
    /// The address the page last navigated to, or an empty string when it has not navigated.
    /// </summary>
    public string Address
    {
        get
        {
            lock (_lock)
            {
                return _address;
            }
        }
    }

    /// <summary>
    /// Records the process rendering the page, counting a process failure when it goes absent. A negative id
    /// means the platform could not report one and leaves the page's state untouched.
    /// </summary>
    public PageProcessChange RecordProcessId(long processId)
    {
        lock (_lock)
        {
            if (processId < 0)
            {
                return PageProcessChange.None;
            }

            var previousProcessId = _processId;
            _processId = processId;

            if (processId == 0)
            {
                // The renderer stays absent across later wakes, so its death counts once.
                if (previousProcessId <= 0)
                {
                    return PageProcessChange.None;
                }

                _processFailures++;
                return PageProcessChange.Gone;
            }

            if (previousProcessId == 0)
            {
                // The death this replaces was counted when the renderer went absent.
                return PageProcessChange.Relaunched;
            }

            if (previousProcessId == UnknownProcessId
                || previousProcessId == processId)
            {
                return PageProcessChange.None;
            }

            // Not a failure: one running renderer gave way to another, which is what WebKit does to a page
            // it has suspended in the background. Only a renderer observed absent counts.
            return PageProcessChange.Replaced;
        }
    }

    public WebViewHealth GetHealth()
    {
        lock (_lock)
        {
            return new WebViewHealth(_wakeFailures, _processFailures);
        }
    }
}
