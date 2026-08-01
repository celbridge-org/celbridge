using Celbridge.Console.Services;

namespace Celbridge.Tests.Console;

[TestFixture]
public class ConsoleTriggerSchedulerTests
{
    private static readonly Guid SessionA = Guid.NewGuid();
    private static readonly Guid SessionB = Guid.NewGuid();

    // Hands out a wait the test completes by hand, standing in for the clock. The completion sources run
    // their continuations inline, so completing one drives the rest of the scheduler synchronously and the
    // assertions never race it.
    private sealed class DebounceClock
    {
        private readonly List<TaskCompletionSource> _waits = new();

        public Task DelayAsync(int debounceMilliseconds)
        {
            var wait = new TaskCompletionSource();
            _waits.Add(wait);

            return wait.Task;
        }

        /// Completes one outstanding wait, as the clock reaching that request's debounce period would.
        public void Elapse(int waitIndex)
        {
            _waits[waitIndex].TrySetResult();
        }

        public void ElapseAll()
        {
            foreach (var wait in _waits.ToList())
            {
                wait.TrySetResult();
            }
        }
    }

    private static (ConsoleTriggerScheduler Scheduler, DebounceClock Clock, List<string> Fired) Build()
    {
        var clock = new DebounceClock();
        var fired = new List<string>();
        var scheduler = new ConsoleTriggerScheduler(
            (sessionId, invocation) => fired.Add(invocation),
            clock.DelayAsync);

        return (scheduler, clock, fired);
    }

    [Test]
    public void Schedule_RepeatsOfTheSameCommand_RunItOnceAfterTheChangesStop()
    {
        var (scheduler, clock, fired) = Build();

        scheduler.Schedule(SessionA, "%run clean_data.py");
        scheduler.Schedule(SessionA, "%run clean_data.py");
        scheduler.Schedule(SessionA, "%run clean_data.py");

        clock.ElapseAll();

        fired.Should().Equal("%run clean_data.py");
    }

    [Test]
    public void Schedule_ARepeatDuringTheWait_RestartsIt()
    {
        // The point of the debounce: a resource still being written pushes the run back rather than letting
        // it fire part way through the writing.
        var (scheduler, clock, fired) = Build();

        scheduler.Schedule(SessionA, "!make");
        scheduler.Schedule(SessionA, "!make");

        // The first request's wait is now the superseded one, so its clock reaching the period runs nothing.
        clock.Elapse(0);
        fired.Should().BeEmpty();

        clock.Elapse(1);
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

        clock.ElapseAll();

        fired.Should().BeEquivalentTo("%run \"a.py\"", "%run \"b.py\"");
    }

    [Test]
    public void Schedule_SameCommandInDifferentSessions_RunsInEach()
    {
        var (scheduler, clock, fired) = Build();

        scheduler.Schedule(SessionA, "!make");
        scheduler.Schedule(SessionB, "!make");

        clock.ElapseAll();

        fired.Should().Equal("!make", "!make");
    }

    [Test]
    public void Schedule_AfterACommandHasRun_StartsAFreshWait()
    {
        // The pending request is cleared before the command fires, so a change arriving while it runs is not
        // absorbed by the wait that is already spent.
        var (scheduler, clock, fired) = Build();

        scheduler.Schedule(SessionA, "!make");
        clock.Elapse(0);

        scheduler.Schedule(SessionA, "!make");
        clock.Elapse(1);

        fired.Should().Equal("!make", "!make");
    }
}
