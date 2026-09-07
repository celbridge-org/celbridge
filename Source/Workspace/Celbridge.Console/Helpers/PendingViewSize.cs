namespace Celbridge.Console.Helpers;

/// <summary>
/// A terminal's size in character cells.
/// </summary>
public sealed record TerminalSize(int Cols, int Rows);

/// <summary>
/// The terminal size a console view reports, held for a launch that has to know it before it creates the pty.
/// The first size reported is the one every waiter receives, so a launch and the view it is waiting for can
/// arrive in either order.
/// </summary>
public sealed class PendingViewSize
{
    private readonly TaskCompletionSource<TerminalSize> _reported =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

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

        _reported.TrySetResult(new TerminalSize(cols, rows));
    }

    /// <summary>
    /// Waits for a view to report a size, returning null when none arrives within the timeout.
    /// </summary>
    public async Task<TerminalSize?> WaitAsync(int timeoutMs)
    {
        var completed = await Task.WhenAny(_reported.Task, Task.Delay(timeoutMs));
        if (completed != _reported.Task)
        {
            return null;
        }

        return await _reported.Task;
    }
}
