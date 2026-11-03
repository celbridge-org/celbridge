namespace Celbridge.UserInterface.Helpers;

/// <summary>
/// Times the fades of a pulsing status pip from the system clock. A fade starts at each whole multiple of the
/// period, so every pulsing pip fades at the same moments with no shared state.
/// </summary>
public static class PulseSchedule
{
    /// <summary>
    /// Returns the delay from now to the first whole multiple of the period later than now plus notBefore. The
    /// caller passes now as the system clock's reading in milliseconds.
    /// </summary>
    public static TimeSpan GetDelayToNextFade(long now, TimeSpan notBefore, TimeSpan period)
    {
        var periodMilliseconds = (long)period.TotalMilliseconds;
        var earliest = now + (long)notBefore.TotalMilliseconds;
        var due = earliest - (earliest % periodMilliseconds) + periodMilliseconds;

        return TimeSpan.FromMilliseconds(due - now);
    }
}
