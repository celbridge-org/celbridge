namespace Celbridge.Resources.Helpers;

/// <summary>
/// A point in the chronological order of a root resolver's folder listings. Each listing takes a later timestamp than
/// every listing that started before it.
/// </summary>
internal readonly record struct ListingTimestamp(long Value)
{
    /// <summary>
    /// Returns true if this timestamp is later than the other.
    /// </summary>
    public bool IsAfter(ListingTimestamp other)
    {
        return Value > other.Value;
    }
}

/// <summary>
/// A logical clock for a folder cache's listings. A starting listing advances the clock and takes the new timestamp.
/// A listing with an earlier timestamp started before it.
/// </summary>
internal sealed class ListingClock
{
    private long _value;

    /// <summary>
    /// Returns the current timestamp. A listing that starts from now on takes a later one.
    /// </summary>
    public ListingTimestamp Now()
    {
        return new ListingTimestamp(Interlocked.Read(ref _value));
    }

    /// <summary>
    /// Advances the clock and returns the new timestamp, for a listing that is starting.
    /// </summary>
    public ListingTimestamp Advance()
    {
        return new ListingTimestamp(Interlocked.Increment(ref _value));
    }
}
