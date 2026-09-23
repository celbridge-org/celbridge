using Celbridge.WebHost;

namespace Celbridge.Tests.WebHost;

/// <summary>
/// NavigationStarting is where every head but macOS decides a navigation, and both Uno and WebView2 raise it
/// with the request for the navigation already made ready. These tests pin what the gate is asked about and
/// what it reports, which is what a head that has to stop the request as well acts on.
/// </summary>
[TestFixture]
public class NavigationStartingGateTests
{
    private static readonly Uri Destination = new("https://example.com/elsewhere");

    private List<Uri> _refused = null!;

    [SetUp]
    public void SetUp()
    {
        _refused = new List<Uri>();
    }

    [Test]
    public void ANavigationTheGateAllows_GoesAhead_AndIsNotReported()
    {
        var goesAhead = NavigationStartingGate.Decides(
            Destination.AbsoluteUri, isUserInitiated: true, Gate(allow: true), _refused.Add);

        goesAhead.Should().BeTrue();
        _refused.Should().BeEmpty();
    }

    [Test]
    public void ANavigationTheGateRefuses_DoesNotGoAhead_AndIsReported()
    {
        var goesAhead = NavigationStartingGate.Decides(
            Destination.AbsoluteUri, isUserInitiated: true, Gate(allow: false), _refused.Add);

        goesAhead.Should().BeFalse();
        _refused.Should().Equal(Destination);
    }

    [Test]
    public void ARefusalOnAHeadWithNothingToStop_IsNotReportedAnywhere()
    {
        var goesAhead = NavigationStartingGate.Decides(
            Destination.AbsoluteUri, isUserInitiated: true, Gate(allow: false), onRefused: null);

        goesAhead.Should().BeFalse();
    }

    [Test]
    public void TheGateIsToldWhoStartedTheNavigation()
    {
        var startedByTheUser = new List<bool>();

        NavigationStartingGate.Decides(
            Destination.AbsoluteUri,
            isUserInitiated: false,
            (_, isUserInitiated) =>
            {
                startedByTheUser.Add(isUserInitiated);
                return true;
            },
            _refused.Add);

        startedByTheUser.Should().Equal(false);
    }

    [Test]
    public void ANavigationToNoAddressAtAll_GoesAheadUnasked()
    {
        var wasAsked = false;

        var goesAhead = NavigationStartingGate.Decides(
            string.Empty,
            isUserInitiated: true,
            (_, _) =>
            {
                wasAsked = true;
                return false;
            },
            _refused.Add);

        goesAhead.Should().BeTrue();
        wasAsked.Should().BeFalse();
    }

    [Test]
    public void ANavigationToAnAddressThatIsNotAbsolute_GoesAheadUnasked()
    {
        var wasAsked = false;

        // Not "/elsewhere": a leading slash parses as an absolute file URL everywhere but Windows, so the
        // gate would be asked about file:///elsewhere and this case would test nothing.
        var goesAhead = NavigationStartingGate.Decides(
            "elsewhere",
            isUserInitiated: true,
            (_, _) =>
            {
                wasAsked = true;
                return false;
            },
            _refused.Add);

        goesAhead.Should().BeTrue();
        wasAsked.Should().BeFalse();
    }

    private static NavigationGate Gate(bool allow)
    {
        return (_, _) => allow;
    }
}
