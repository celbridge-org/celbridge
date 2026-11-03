using Celbridge.UserInterface.Helpers;

namespace Celbridge.Tests.UserInterface;

/// <summary>
/// Tests for the timing of a pulsing pip's fades. The timing is a pure function of the clock, so these run
/// without a pip.
/// </summary>
[TestFixture]
public class PulseScheduleTests
{
    private static readonly TimeSpan Period = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan FadeDuration = TimeSpan.FromSeconds(1);

    [TestCase(0, 4000)]
    [TestCase(1, 3999)]
    [TestCase(3999, 1)]
    [TestCase(4000, 4000)]
    [TestCase(10500, 1500)]
    public void APipThatStartsToPulse_WaitsForTheNextMultipleOfThePeriod(long now, long expectedDelay)
    {
        var delay = PulseSchedule.GetDelayToNextFade(now, TimeSpan.Zero, Period);

        delay.Should().Be(TimeSpan.FromMilliseconds(expectedDelay));
    }

    [Test]
    public void ATickThatLandsEarly_WaitsForTheMultipleAfterTheOneItFadedFor()
    {
        // The timer fires 5 ms before the multiple at 8000, and this tick plays the fade for 8000.
        var delay = PulseSchedule.GetDelayToNextFade(7995, FadeDuration, Period);

        delay.Should().Be(TimeSpan.FromMilliseconds(12000 - 7995));
    }

    [Test]
    public void ATickThatLandsLate_WaitsForTheNextMultiple()
    {
        var delay = PulseSchedule.GetDelayToNextFade(8050, FadeDuration, Period);

        delay.Should().Be(TimeSpan.FromMilliseconds(12000 - 8050));
    }
}
