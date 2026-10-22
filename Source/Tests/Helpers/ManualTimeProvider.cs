namespace Celbridge.Tests.Helpers;

/// <summary>
/// A TimeProvider for tests. The time moves only when a test calls Advance. Advancing the time fires each timer that
/// falls due, in the order of the due times, on the test's thread.
/// </summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private readonly List<ManualTimer> _timers = new();
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _now.UtcTicks;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);

        lock (_timers)
        {
            _timers.Add(timer);
        }

        return timer;
    }

    /// <summary>
    /// Moves the time forward by the interval. A timer that a callback creates fires too, when it falls due within
    /// the interval.
    /// </summary>
    public void Advance(TimeSpan interval)
    {
        var target = _now + interval;

        while (true)
        {
            ManualTimer? nextTimer;
            lock (_timers)
            {
                nextTimer = _timers
                    .Where(timer => timer.DueAt is not null && timer.DueAt <= target)
                    .OrderBy(timer => timer.DueAt)
                    .FirstOrDefault();
            }

            if (nextTimer is null)
            {
                break;
            }

            if (nextTimer.DueAt > _now)
            {
                _now = nextTimer.DueAt.Value;
            }

            nextTimer.Fire();
        }

        _now = target;
    }

    private void RemoveTimer(ManualTimer timer)
    {
        lock (_timers)
        {
            _timers.Remove(timer);
        }
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly ManualTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private TimeSpan _period = Timeout.InfiniteTimeSpan;
        private bool _isDisposed;

        public ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
        }

        public DateTimeOffset? DueAt { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (_isDisposed)
            {
                return false;
            }

            _period = period;
            if (dueTime == Timeout.InfiniteTimeSpan)
            {
                DueAt = null;
            }
            else
            {
                DueAt = _owner._now + dueTime;
            }

            return true;
        }

        public void Fire()
        {
            if (_period == Timeout.InfiniteTimeSpan)
            {
                DueAt = null;
            }
            else
            {
                DueAt += _period;
            }

            _callback(_state);
        }

        public void Dispose()
        {
            _isDisposed = true;
            DueAt = null;
            _owner.RemoveTimer(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
