using Celbridge.Documents.Helpers;

namespace Celbridge.Tests.Documents;

/// <summary>
/// Covers WheelGestureAxisTracker: which of a gesture's events count as travelling along the axis it
/// scrolls on, and how a pause in the events starts a fresh gesture that can take the other axis.
/// </summary>
[TestFixture]
public class WheelGestureAxisTrackerTests
{
    private const ulong GestureStart = 1_000_000;
    private const ulong EventInterval = 10_000;
    private const ulong LongerThanGestureGap = 300_000;

    private WheelGestureAxisTracker _tracker = null!;

    [SetUp]
    public void Setup()
    {
        _tracker = new WheelGestureAxisTracker();
    }

    [Test]
    public void MouseWheelNotches_AllTravelAlongTheGestureAxis()
    {
        ulong timestamp = GestureStart;

        for (int notch = 0; notch < 5; notch++)
        {
            bool isOnGestureAxis = _tracker.IsOnGestureAxis(timestamp, isHorizontalWheel: false, wheelDelta: -30);

            isOnGestureAxis.Should().BeTrue();
            timestamp += EventInterval;
        }
    }

    [Test]
    public void HorizontalSwipe_RejectsTheVerticalDriftBetweenItsEvents()
    {
        ulong timestamp = GestureStart;

        // The first event of the swipe establishes the horizontal axis.
        _tracker.IsOnGestureAxis(timestamp, isHorizontalWheel: true, wheelDelta: -1).Should().BeTrue();

        // A slow swipe travels a pixel at a time, so its drift is the same size as the swipe itself.
        for (int index = 0; index < 4; index++)
        {
            timestamp += EventInterval;
            _tracker.IsOnGestureAxis(timestamp, isHorizontalWheel: false, wheelDelta: 1).Should().BeFalse();

            timestamp += EventInterval;
            _tracker.IsOnGestureAxis(timestamp, isHorizontalWheel: true, wheelDelta: -1).Should().BeTrue();
        }
    }

    [Test]
    public void VerticalSwipe_RejectsTheHorizontalDriftBetweenItsEvents()
    {
        ulong timestamp = GestureStart;

        _tracker.IsOnGestureAxis(timestamp, isHorizontalWheel: false, wheelDelta: -8).Should().BeTrue();

        timestamp += EventInterval;
        _tracker.IsOnGestureAxis(timestamp, isHorizontalWheel: true, wheelDelta: 1).Should().BeFalse();

        timestamp += EventInterval;
        _tracker.IsOnGestureAxis(timestamp, isHorizontalWheel: false, wheelDelta: -6).Should().BeTrue();
    }

    [Test]
    public void PauseAfterASwipe_LetsTheNextGestureTakeTheOtherAxis()
    {
        ulong timestamp = GestureStart;

        for (int index = 0; index < 5; index++)
        {
            _tracker.IsOnGestureAxis(timestamp, isHorizontalWheel: true, wheelDelta: -20).Should().BeTrue();
            timestamp += EventInterval;
        }

        // Without the pause resetting the gesture, the horizontal travel already recorded would outweigh
        // this event and reject it.
        timestamp += LongerThanGestureGap;
        _tracker.IsOnGestureAxis(timestamp, isHorizontalWheel: false, wheelDelta: -10).Should().BeTrue();
    }
}
