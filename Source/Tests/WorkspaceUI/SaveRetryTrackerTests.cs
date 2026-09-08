using Celbridge.Resources;
using Celbridge.Workspace;
using Celbridge.WorkspaceUI.Services;

namespace Celbridge.Tests.WorkspaceUI;

/// <summary>
/// Tests for the backoff held against each resource that is waiting to be written again.
/// </summary>
[TestFixture]
public class SaveRetryTrackerTests
{
    private static readonly ResourceKey Resource = new("project:notes.txt");

    private SaveRetryTracker _tracker = null!;

    [SetUp]
    public void Setup()
    {
        _tracker = new SaveRetryTracker();
    }

    [Test]
    public void Schedule_StartsTheResourceWaiting()
    {
        _tracker.Schedule(Resource, "locked");

        _tracker.IsRetrying(Resource).Should().BeTrue();
        _tracker.IsWaiting(Resource).Should().BeTrue();
    }

    [Test]
    public void Schedule_DoublesTheWaitAfterEachFailure()
    {
        _tracker.Schedule(Resource, "locked");
        _tracker.UpdateTimers(SaveConstants.InitialRetryDelay);
        _tracker.IsWaiting(Resource).Should().BeFalse("the first wait is the initial delay");

        _tracker.Schedule(Resource, "locked");
        _tracker.UpdateTimers(SaveConstants.InitialRetryDelay);
        _tracker.IsWaiting(Resource).Should().BeTrue("the second wait is twice the first");
    }

    [Test]
    public void Schedule_CapsTheWaitAtTheMaximum()
    {
        // Ten failures would run to hundreds of seconds if the wait were left to double unchecked.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            _tracker.Schedule(Resource, "locked");
        }

        _tracker.UpdateTimers(SaveConstants.MaximumRetryDelay);

        _tracker.IsWaiting(Resource).Should().BeFalse();
    }

    [Test]
    public void Schedule_ReportsWhetherTheReasonChanged()
    {
        _tracker.Schedule(Resource, "locked").Should().BeTrue("the first attempt has no previous reason");
        _tracker.Schedule(Resource, "locked").Should().BeFalse();
        _tracker.Schedule(Resource, "read-only").Should().BeTrue();
    }

    [Test]
    public void Forget_DropsEverythingHeldAboutTheResource()
    {
        _tracker.Schedule(Resource, "locked");

        _tracker.Forget(Resource);

        _tracker.IsRetrying(Resource).Should().BeFalse();
        _tracker.IsWaiting(Resource).Should().BeFalse();
    }

    [Test]
    public void ForgetAllExcept_DropsTheResourcesThatAreGone()
    {
        var closedResource = new ResourceKey("project:closed.txt");
        _tracker.Schedule(Resource, "locked");
        _tracker.Schedule(closedResource, "locked");

        _tracker.ForgetAllExcept(new HashSet<ResourceKey> { Resource });

        _tracker.IsRetrying(Resource).Should().BeTrue();
        _tracker.IsRetrying(closedResource).Should().BeFalse();
    }
}
