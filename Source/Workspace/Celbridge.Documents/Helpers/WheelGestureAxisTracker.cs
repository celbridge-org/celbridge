namespace Celbridge.Documents.Helpers;

/// <summary>
/// Follows a wheel gesture across the events it arrives as, and reports which of them travel along the axis
/// that gesture is scrolling on. macOS puts each trackpad event on one axis only, dropping the travel along
/// the other, so a swipe arrives interleaved with events carrying nothing but the finger's drift.
/// </summary>
public class WheelGestureAxisTracker
{
    // A gap this long in the events ends the gesture they belong to, in the microseconds pointer timestamps
    // are counted in.
    private const ulong GestureGapMicroseconds = 200_000;

    private ulong _lastTimestamp;
    private double _horizontalDistance;
    private double _verticalDistance;

    /// <summary>
    /// Records a wheel event and reports whether it travels along the gesture's axis. The axis that has
    /// travelled furthest since the gesture began is the one it is scrolling on, so the drift events are
    /// rejected however many of them arrive. A tie goes to the horizontal axis.
    /// </summary>
    public bool IsOnGestureAxis(ulong timestamp, bool isHorizontalWheel, int wheelDelta)
    {
        if (timestamp - _lastTimestamp > GestureGapMicroseconds)
        {
            _horizontalDistance = 0;
            _verticalDistance = 0;
        }

        _lastTimestamp = timestamp;

        double distance = Math.Abs(wheelDelta);
        if (isHorizontalWheel)
        {
            _horizontalDistance += distance;

            return _horizontalDistance >= _verticalDistance;
        }

        _verticalDistance += distance;

        return _verticalDistance > _horizontalDistance;
    }
}
