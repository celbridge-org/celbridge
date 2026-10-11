using Celbridge.WebHost;
using Celbridge.WebHost.Services;

namespace Celbridge.Tests.WebHost;

/// <summary>
/// Unit tests for a web view's health tracker.
/// </summary>
[TestFixture]
public class WebViewHealthTrackerTests
{
    private WebViewHealthTracker _tracker = null!;

    [SetUp]
    public void Setup()
    {
        _tracker = new WebViewHealthTracker();
    }

    [Test]
    public void NewTracker_StartsHealthy()
    {
        _tracker.GetHealth().Should().Be(WebViewHealth.Healthy);
        _tracker.GetHealth().IsHealthy.Should().BeTrue();
    }

    [Test]
    public void MissedWakes_AccumulateAndReportUnhealthy()
    {
        _tracker.RecordWakeFailed().Should().Be(1);
        _tracker.RecordWakeFailed().Should().Be(2);

        var health = _tracker.GetHealth();
        health.WakeFailures.Should().Be(2);
        health.IsHealthy.Should().BeFalse();
    }

    [Test]
    public void SuccessfulWake_ClearsMissedWakesAndReportsHowMany()
    {
        _tracker.RecordWakeFailed();
        _tracker.RecordWakeFailed();

        _tracker.RecordWakeSucceeded().Should().Be(2);
        _tracker.GetHealth().WakeFailures.Should().Be(0);

        // Only a page that missed a wake can report a recovery.
        _tracker.RecordWakeSucceeded().Should().Be(0);
    }

    [Test]
    public void ProcessFailures_SurviveASuccessfulWake()
    {
        _tracker.RecordProcessId(100);
        _tracker.RecordProcessId(0);
        _tracker.RecordProcessId(200);

        _tracker.RecordWakeSucceeded();

        // The wake succeeds against the relaunched process, but the earlier process death still counts.
        _tracker.GetHealth().ProcessFailures.Should().Be(1);
    }

    [Test]
    public void ReportedProcessFailures_AddToTheObservedOnes()
    {
        _tracker.RecordProcessId(100);
        _tracker.RecordProcessId(0);

        _tracker.RecordProcessFailed();

        _tracker.GetHealth().ProcessFailures.Should().Be(2);
    }

    [Test]
    public void FirstProcessIdReading_IsNotAChange()
    {
        _tracker.RecordProcessId(100).Should().Be(PageProcessChange.None);
        _tracker.GetHealth().ProcessFailures.Should().Be(0);
    }

    [Test]
    public void UnchangedProcessId_IsNotAChange()
    {
        _tracker.RecordProcessId(100);

        _tracker.RecordProcessId(100).Should().Be(PageProcessChange.None);
        _tracker.GetHealth().ProcessFailures.Should().Be(0);
    }

    [Test]
    public void ReplacedProcess_IsReportedWithoutCountingAFailure()
    {
        // WebKit can give a page it suspended in the background a new renderer. The page goes on working, so
        // the swap is reported without marking the document unhealthy.
        _tracker.RecordProcessId(100);

        _tracker.RecordProcessId(200).Should().Be(PageProcessChange.Replaced);
        _tracker.GetHealth().ProcessFailures.Should().Be(0);
    }

    [Test]
    public void ProcessChangeAfterANavigation_IsNotAChange()
    {
        // A navigation swaps the prewarmed process for the page's own. No renderer failed.
        _tracker.RecordProcessId(100);
        _tracker.RecordNavigation("https://example.com/");

        _tracker.RecordProcessId(200).Should().Be(PageProcessChange.None);
        _tracker.Address.Should().Be("https://example.com/");
    }

    [Test]
    public void AbsentProcess_CountsOneFailureAndIsNotRecounted()
    {
        _tracker.RecordProcessId(100);

        _tracker.RecordProcessId(0).Should().Be(PageProcessChange.Gone);
        _tracker.RecordProcessId(0).Should().Be(PageProcessChange.None);

        _tracker.GetHealth().ProcessFailures.Should().Be(1);
    }

    [Test]
    public void ProcessAbsentThenBack_CountsTheDeathOnlyOnce()
    {
        _tracker.RecordProcessId(100);
        _tracker.RecordProcessId(0);

        _tracker.RecordProcessId(300).Should().Be(PageProcessChange.Relaunched);
        _tracker.GetHealth().ProcessFailures.Should().Be(1);
    }

    [Test]
    public void ProcessAbsentOnFirstReading_IsNotAFailure()
    {
        // Process id 0 means the renderer is still starting, which leaves the failure count at zero.
        _tracker.RecordProcessId(0).Should().Be(PageProcessChange.None);
        _tracker.GetHealth().ProcessFailures.Should().Be(0);
    }

    [Test]
    public void UnreadableProcessId_LeavesTheLastKnownProcessInPlace()
    {
        _tracker.RecordProcessId(100);

        // Process id -1 means the head cannot read the id, which leaves the last known renderer in place.
        _tracker.RecordProcessId(-1).Should().Be(PageProcessChange.None);
        _tracker.GetHealth().ProcessFailures.Should().Be(0);

        _tracker.RecordProcessId(100).Should().Be(PageProcessChange.None);
        _tracker.GetHealth().ProcessFailures.Should().Be(0);
    }
}
