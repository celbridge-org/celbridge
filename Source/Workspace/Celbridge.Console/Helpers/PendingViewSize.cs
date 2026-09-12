namespace Celbridge.Console.Helpers;

/// <summary>
/// A terminal's size in character cells.
/// </summary>
public sealed record TerminalSize(int Cols, int Rows);

/// <summary>
/// The terminal size a console view reports, held for a launch that has to know it before it creates the pty.
/// A launch and the view it is waiting for can arrive in either order. A view reports again as its layout
/// settles, so the wait returns the size those reports settle on rather than the first of them.
/// </summary>
public sealed class PendingViewSize
{
    // How long reports must stop for before the size counts as settled. A view that is still being laid out,
    // or one being laid out against the size its section will give it, reports again within this window, and
    // a pty created at a size the layout has already moved on from has to be resized once the view is shown,
    // which costs the screen the output painted on it.
    private const int SettleMs = 250;

    private readonly object _lock = new();

    private readonly TaskCompletionSource _reported =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private TerminalSize? _reportedSize;
    private bool _sizeUnavailable;

    /// <summary>
    /// The last size a view reported, or null when none has. Read after the pty exists to pick up a size
    /// that arrived while it was being created, which found no terminal to apply it to.
    /// </summary>
    public TerminalSize? Current
    {
        get
        {
            lock (_lock)
            {
                return _reportedSize;
            }
        }
    }

    /// <summary>
    /// Records the size a view reports. A view that has not been arranged yet reports no size at all, which
    /// is not a size a pty can be created at, so only a positive one counts.
    /// </summary>
    public void Report(int cols, int rows)
    {
        if (cols <= 0 ||
            rows <= 0)
        {
            return;
        }

        lock (_lock)
        {
            _reportedSize = new TerminalSize(cols, rows);
        }

        _reported.TrySetResult();
    }

    /// <summary>
    /// Records that no view will report a size, which ends the wait at once rather than leaving a launch
    /// holding for a timeout it is already known to reach.
    /// </summary>
    public void ReportUnavailable()
    {
        lock (_lock)
        {
            _sizeUnavailable = true;
        }

        _reported.TrySetResult();
    }

    /// <summary>
    /// Waits for a view to report a size and for its reports to settle, returning null when none arrives
    /// within the timeout. A size still changing when the timeout runs out is returned as it stands.
    /// </summary>
    public async Task<TerminalSize?> WaitAsync(int timeoutMs)
    {
        var deadline = Task.Delay(timeoutMs);

        var firstReport = await Task.WhenAny(_reported.Task, deadline);
        if (firstReport == deadline)
        {
            return null;
        }

        while (true)
        {
            TerminalSize? sizeBeforeSettling;
            lock (_lock)
            {
                sizeBeforeSettling = _reportedSize;

                // No size was reported and none is coming, so there is nothing to settle.
                if (_sizeUnavailable &&
                    sizeBeforeSettling is null)
                {
                    return null;
                }
            }

            var settled = await Task.WhenAny(Task.Delay(SettleMs), deadline);

            lock (_lock)
            {
                if (settled == deadline ||
                    _reportedSize == sizeBeforeSettling)
                {
                    return _reportedSize;
                }
            }
        }
    }
}
