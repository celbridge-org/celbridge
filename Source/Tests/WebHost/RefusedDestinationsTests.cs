using Celbridge.WebHost.Platform;

namespace Celbridge.Tests.WebHost;

/// <summary>
/// WebView2 has the request for a navigation ready as it raises NavigationStarting, and sends it even though
/// the navigation is cancelled there, so what the user refuses is recorded and the request that follows is
/// stopped by it. These tests pin which request a refusal stops, and for how long.
/// </summary>
[TestFixture]
public class RefusedDestinationsTests
{
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan interval) => _now += interval;
    }

    private FakeTimeProvider _timeProvider = null!;
    private RefusedDestinations _refusedDestinations = null!;

    [SetUp]
    public void SetUp()
    {
        _timeProvider = new FakeTimeProvider();
        _refusedDestinations = new RefusedDestinations(_timeProvider);
    }

    [Test]
    public void TheRequestForADestinationJustRefused_IsStopped()
    {
        _refusedDestinations.Refuse(new Uri("https://example.com/elsewhere"));

        _refusedDestinations.Stops("https://example.com/elsewhere").Should().BeTrue();
    }

    [Test]
    public void ARequestForAnotherDestination_IsNotStopped()
    {
        _refusedDestinations.Refuse(new Uri("https://example.com/elsewhere"));

        _refusedDestinations.Stops("https://example.com/page").Should().BeFalse();
    }

    [Test]
    public void ARequestForADestinationNothingRefused_IsNotStopped()
    {
        _refusedDestinations.Stops("https://example.com/page").Should().BeFalse();
    }

    [Test]
    public void ARefusalIsSpentOnTheRequestItStops_SoTheSameDestinationAskedForAgain_IsFetched()
    {
        _refusedDestinations.Refuse(new Uri("https://example.com/elsewhere"));

        _refusedDestinations.Stops("https://example.com/elsewhere").Should().BeTrue();
        _refusedDestinations.Stops("https://example.com/elsewhere").Should().BeFalse();
    }

    [Test]
    public void ARefusalNothingCameFor_StopsNothingOnceItsMomentHasPassed()
    {
        _refusedDestinations.Refuse(new Uri("https://example.com/elsewhere"));

        _timeProvider.Advance(TimeSpan.FromSeconds(30));

        _refusedDestinations.Stops("https://example.com/elsewhere").Should().BeFalse();
    }

    [Test]
    public void ARequestNamingTheRefusedDestinationInAnotherForm_IsStopped()
    {
        _refusedDestinations.Refuse(new Uri("https://example.com:443/elsewhere"));

        _refusedDestinations.Stops("https://example.com/elsewhere").Should().BeTrue();
    }

    [Test]
    public void EachOfSeveralRefusedDestinations_StopsItsOwnRequest()
    {
        _refusedDestinations.Refuse(new Uri("https://example.com/first"));
        _refusedDestinations.Refuse(new Uri("https://example.com/second"));

        _refusedDestinations.Stops("https://example.com/second").Should().BeTrue();
        _refusedDestinations.Stops("https://example.com/first").Should().BeTrue();
    }

    [Test]
    public void ARequestThatNamesNoAbsoluteAddress_IsNotStopped()
    {
        _refusedDestinations.Refuse(new Uri("https://example.com/elsewhere"));

        _refusedDestinations.Stops("/elsewhere").Should().BeFalse();
    }
}
