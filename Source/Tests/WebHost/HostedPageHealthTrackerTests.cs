using Celbridge.WebHost.Services;

namespace Celbridge.Tests.WebHost;

/// <summary>
/// Unit tests for the hosted page health tracker. The tracker is keyed by the page it describes, so these
/// tests stand in a plain object for the CoreWebView2 the adapter passes.
/// </summary>
[TestFixture]
public class HostedPageHealthTrackerTests
{
    private HostedPageHealthTracker<object> _tracker = null!;
    private object _page = null!;

    [SetUp]
    public void Setup()
    {
        _tracker = new HostedPageHealthTracker<object>();
        _page = new object();
        _tracker.Track(_page);
    }

    [Test]
    public void TrackedPage_StartsHealthy()
    {
        _tracker.GetHealth(_page).Should().Be(DocumentHealth.Healthy);
        _tracker.GetHealth(_page).IsHealthy.Should().BeTrue();
    }

    [Test]
    public void MissedWakes_AccumulateAndReportUnhealthy()
    {
        _tracker.RecordWakeFailed(_page).Should().Be(1);
        _tracker.RecordWakeFailed(_page).Should().Be(2);

        var health = _tracker.GetHealth(_page);
        health.WakeFailures.Should().Be(2);
        health.IsHealthy.Should().BeFalse();
    }

    [Test]
    public void SuccessfulWake_ClearsMissedWakesAndReportsHowMany()
    {
        _tracker.RecordWakeFailed(_page);
        _tracker.RecordWakeFailed(_page);

        _tracker.RecordWakeSucceeded(_page).Should().Be(2);
        _tracker.GetHealth(_page).WakeFailures.Should().Be(0);

        // A page that never missed a wake has nothing to report as recovered.
        _tracker.RecordWakeSucceeded(_page).Should().Be(0);
    }

    [Test]
    public void ProcessFailures_SurviveASuccessfulWake()
    {
        _tracker.RecordProcessId(_page, 100);
        _tracker.RecordProcessId(_page, 200);

        _tracker.RecordWakeSucceeded(_page);

        // The wake succeeds against the replacement process, which does not undo the crash it replaced.
        _tracker.GetHealth(_page).ProcessFailures.Should().Be(1);
    }

    [Test]
    public void FirstProcessIdReading_IsNotAChange()
    {
        _tracker.RecordProcessId(_page, 100).Should().Be(HostedPageProcessChange.None);
        _tracker.GetHealth(_page).ProcessFailures.Should().Be(0);
    }

    [Test]
    public void UnchangedProcessId_IsNotAChange()
    {
        _tracker.RecordProcessId(_page, 100);

        _tracker.RecordProcessId(_page, 100).Should().Be(HostedPageProcessChange.None);
        _tracker.GetHealth(_page).ProcessFailures.Should().Be(0);
    }

    [Test]
    public void ReplacedProcess_CountsOneFailure()
    {
        _tracker.RecordProcessId(_page, 100);

        _tracker.RecordProcessId(_page, 200).Should().Be(HostedPageProcessChange.Replaced);
        _tracker.GetHealth(_page).ProcessFailures.Should().Be(1);
    }

    [Test]
    public void AbsentProcess_CountsOneFailureAndIsNotRecounted()
    {
        _tracker.RecordProcessId(_page, 100);

        _tracker.RecordProcessId(_page, 0).Should().Be(HostedPageProcessChange.Gone);
        _tracker.RecordProcessId(_page, 0).Should().Be(HostedPageProcessChange.None);

        _tracker.GetHealth(_page).ProcessFailures.Should().Be(1);
    }

    [Test]
    public void ProcessAbsentThenBack_CountsTheDeathOnlyOnce()
    {
        _tracker.RecordProcessId(_page, 100);
        _tracker.RecordProcessId(_page, 0);

        _tracker.RecordProcessId(_page, 300).Should().Be(HostedPageProcessChange.Relaunched);
        _tracker.GetHealth(_page).ProcessFailures.Should().Be(1);
    }

    [Test]
    public void ProcessAbsentOnFirstReading_IsNotAFailure()
    {
        // A page whose renderer has not started yet has not lost one.
        _tracker.RecordProcessId(_page, 0).Should().Be(HostedPageProcessChange.None);
        _tracker.GetHealth(_page).ProcessFailures.Should().Be(0);
    }

    [Test]
    public void UnreadableProcessId_LeavesTheLastKnownProcessInPlace()
    {
        _tracker.RecordProcessId(_page, 100);

        // A head that cannot report an id must not read as a renderer that has gone away.
        _tracker.RecordProcessId(_page, -1).Should().Be(HostedPageProcessChange.None);
        _tracker.GetHealth(_page).ProcessFailures.Should().Be(0);

        _tracker.RecordProcessId(_page, 100).Should().Be(HostedPageProcessChange.None);
        _tracker.GetHealth(_page).ProcessFailures.Should().Be(0);
    }

    [Test]
    public void UntrackedPage_IsHealthyAndCannotBeCounted()
    {
        var untrackedPage = new object();

        _tracker.RecordWakeFailed(untrackedPage).Should().Be(0);
        _tracker.RecordProcessId(untrackedPage, 100).Should().Be(HostedPageProcessChange.None);

        _tracker.GetHealth(untrackedPage).Should().Be(DocumentHealth.Healthy);
    }

    [Test]
    public void ObservationAfterUntrack_DoesNotResurrectThePage()
    {
        _tracker.RecordWakeFailed(_page);
        _tracker.Untrack(_page);

        // A wake already in flight when the page closed still faults, and must not re-add it.
        _tracker.RecordWakeFailed(_page);

        _tracker.GetHealth(_page).Should().Be(DocumentHealth.Healthy);
    }

    [Test]
    public void RetrackedPage_StartsFromZero()
    {
        _tracker.RecordWakeFailed(_page);
        _tracker.RecordProcessId(_page, 100);
        _tracker.RecordProcessId(_page, 200);

        _tracker.Untrack(_page);
        _tracker.Track(_page);

        _tracker.GetHealth(_page).Should().Be(DocumentHealth.Healthy);
    }

    [Test]
    public void SeparatePages_AreCountedIndependently()
    {
        var otherPage = new object();
        _tracker.Track(otherPage);

        _tracker.RecordWakeFailed(_page);
        _tracker.RecordWakeFailed(_page);

        _tracker.GetHealth(_page).WakeFailures.Should().Be(2);
        _tracker.GetHealth(otherPage).WakeFailures.Should().Be(0);
    }
}
