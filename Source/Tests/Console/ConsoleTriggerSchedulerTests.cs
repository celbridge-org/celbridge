using Celbridge.Console.Services;
using Microsoft.Extensions.Time.Testing;

namespace Celbridge.Tests.Console;

[TestFixture]
public class ConsoleTriggerSchedulerTests
{
    private static readonly Guid SessionA = Guid.NewGuid();
    private static readonly Guid SessionB = Guid.NewGuid();

    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(300);

    private static (ConsoleTriggerScheduler Scheduler, FakeTimeProvider Clock, List<string> Fired) Build()
    {
        var clock = new FakeTimeProvider();
        var fired = new List<string>();
        var scheduler = new ConsoleTriggerScheduler(
            (sessionId, invocation) => fired.Add(invocation),
            clock);

        return (scheduler, clock, fired);
    }

    [Test]
    public void Schedule_RepeatsOfTheSameCommand_RunItOnceAfterTheChangesStop()
    {
        var (scheduler, clock, fired) = Build();

        scheduler.Schedule(SessionA, "%run clean_data.py");
        scheduler.Schedule(SessionA, "%run clean_data.py");
        scheduler.Schedule(SessionA, "%run clean_data.py");

        clock.Advance(DebounceInterval);

        fired.Should().Equal("%run clean_data.py");
    }

    [Test]
    public void Schedule_ARepeatDuringTheWait_RestartsIt()
    {
        // The point of the debounce: a resource still being written pushes the run back rather than letting
        // it fire part way through the writing.
        var (scheduler, clock, fired) = Build();

        scheduler.Schedule(SessionA, "!make");
        clock.Advance(TimeSpan.FromMilliseconds(200));
        scheduler.Schedule(SessionA, "!make");

        // The first request's wait would have ended here, but the repeat restarted it.
        clock.Advance(TimeSpan.FromMilliseconds(200));
        fired.Should().BeEmpty();

        clock.Advance(TimeSpan.FromMilliseconds(100));
        fired.Should().Equal("!make");
    }

    [Test]
    public void Schedule_DistinctCommands_RunSeparately()
    {
        // A trigger command that interpolates the changed resource resolves differently per file, and each
        // of those files still needs its own run.
        var (scheduler, clock, fired) = Build();

        scheduler.Schedule(SessionA, "%run \"a.py\"");
        scheduler.Schedule(SessionA, "%run \"b.py\"");

        clock.Advance(DebounceInterval);

        fired.Should().BeEquivalentTo("%run \"a.py\"", "%run \"b.py\"");
    }

    [Test]
    public void Schedule_SameCommandInDifferentSessions_RunsInEach()
    {
        var (scheduler, clock, fired) = Build();

        scheduler.Schedule(SessionA, "!make");
        scheduler.Schedule(SessionB, "!make");

        clock.Advance(DebounceInterval);

        fired.Should().Equal("!make", "!make");
    }

    [Test]
    public void Schedule_AfterACommandHasRun_StartsAFreshWait()
    {
        // The pending request is cleared before the command fires, so a change arriving while it runs is not
        // absorbed by the wait that is already spent.
        var (scheduler, clock, fired) = Build();

        scheduler.Schedule(SessionA, "!make");
        clock.Advance(DebounceInterval);

        scheduler.Schedule(SessionA, "!make");
        clock.Advance(DebounceInterval - TimeSpan.FromMilliseconds(1));
        fired.Should().Equal("!make");

        clock.Advance(TimeSpan.FromMilliseconds(1));
        fired.Should().Equal("!make", "!make");
    }
}
