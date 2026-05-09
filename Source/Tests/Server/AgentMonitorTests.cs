using System.Threading.Tasks;
using Celbridge.Server.Services;

namespace Celbridge.Tests.Server;

[TestFixture]
public class AgentMonitorTests
{
    private AgentMonitor _monitor = null!;

    [SetUp]
    public void SetUp()
    {
        _monitor = new AgentMonitor();
    }

    // IsBootstrapTool

    [Test]
    public void IsBootstrapTool_ReturnsTrueForGuidesTriad()
    {
        _monitor.IsBootstrapTool("guides_list").Should().BeTrue();
        _monitor.IsBootstrapTool("guides_read").Should().BeTrue();
        _monitor.IsBootstrapTool("guides_search").Should().BeTrue();
    }

    [Test]
    public void IsBootstrapTool_ReturnsFalseForNonBootstrapTools()
    {
        _monitor.IsBootstrapTool("file_read").Should().BeFalse();
        _monitor.IsBootstrapTool("app_get_state").Should().BeFalse();
        _monitor.IsBootstrapTool("guides_unknown").Should().BeFalse();
        _monitor.IsBootstrapTool("").Should().BeFalse();
    }

    // AgentSessionState — TryMarkServed

    [Test]
    public void TryMarkServed_FirstCallReturnsTrue()
    {
        var state = new AgentSessionState("session-1");
        state.TryMarkServed("agent_instructions").Should().BeTrue();
    }

    [Test]
    public void TryMarkServed_SubsequentCallsReturnFalse()
    {
        var state = new AgentSessionState("session-1");
        state.TryMarkServed("agent_instructions").Should().BeTrue();
        state.TryMarkServed("agent_instructions").Should().BeFalse();
        state.TryMarkServed("agent_instructions").Should().BeFalse();
    }

    [Test]
    public void TryMarkServed_RecordsNameInSessionSet()
    {
        var state = new AgentSessionState("session-1");
        state.TryMarkServed("file_grep");
        state.WasGuideRead("file_grep").Should().BeTrue();
        state.WasGuideRead("file_read").Should().BeFalse();
    }

    [Test]
    public void TryMarkServed_DistinctNamesEachReturnTrueOnce()
    {
        var state = new AgentSessionState("session-1");
        state.TryMarkServed("agent_instructions").Should().BeTrue();
        state.TryMarkServed("file").Should().BeTrue();
        state.TryMarkServed("file_read").Should().BeTrue();
    }

    [Test]
    public void MarkGuideRead_RecordsNameInSessionSet()
    {
        var state = new AgentSessionState("session-1");
        state.MarkGuideRead("file_grep");
        state.WasGuideRead("file_grep").Should().BeTrue();
    }

    [Test]
    public void MarkGuideRead_IsIdempotent()
    {
        var state = new AgentSessionState("session-1");
        state.MarkGuideRead("file_grep");
        state.MarkGuideRead("file_grep");
        state.WasGuideRead("file_grep").Should().BeTrue();
    }

    // AgentSessionState — concurrency

    [Test]
    public async Task TryMarkServed_ConcurrentCallsReturnTrueExactlyOnce()
    {
        // Parallel-tool-call race: two threads ask to attach the same namespace
        // guide on the same session. Only one should observe the first-write.
        var state = new AgentSessionState("session-1");

        var trueCount = 0;
        var tasks = new List<Task>();
        for (int taskIndex = 0; taskIndex < 64; taskIndex++)
        {
            tasks.Add(Task.Run(() =>
            {
                if (state.TryMarkServed("file"))
                {
                    Interlocked.Increment(ref trueCount);
                }
            }));
        }
        await Task.WhenAll(tasks);

        trueCount.Should().Be(1);
        state.WasGuideRead("file").Should().BeTrue();
    }

    [Test]
    public async Task MarkGuideRead_ConcurrentCallsAllRecorded()
    {
        var state = new AgentSessionState("session-1");

        var tasks = new List<Task>();
        for (int taskIndex = 0; taskIndex < 64; taskIndex++)
        {
            var capturedIndex = taskIndex;
            tasks.Add(Task.Run(() => state.MarkGuideRead($"guide_{capturedIndex}")));
        }
        await Task.WhenAll(tasks);

        for (int index = 0; index < 64; index++)
        {
            state.WasGuideRead($"guide_{index}").Should().BeTrue();
        }
    }

    // RecordInvocation FIFO eviction

    [Test]
    public void RecordInvocation_EvictsOldestWhenCapExceeded()
    {
        // Cap is 5,000. Push 5,010 records and verify the first 10 dropped out.
        const int cap = 5_000;
        for (int recordIndex = 0; recordIndex < cap + 10; recordIndex++)
        {
            _monitor.RecordInvocation(BuildRecord($"tool_{recordIndex}"));
        }

        var snapshot = _monitor.Invocations;
        snapshot.Count.Should().Be(cap);
        snapshot[0].ToolName.Should().Be("tool_10");
        snapshot[^1].ToolName.Should().Be($"tool_{cap + 9}");
    }

    [Test]
    public void Invocations_ReturnsRecordsInInsertionOrder()
    {
        _monitor.RecordInvocation(BuildRecord("first"));
        _monitor.RecordInvocation(BuildRecord("second"));
        _monitor.RecordInvocation(BuildRecord("third"));

        var snapshot = _monitor.Invocations;
        snapshot.Should().HaveCount(3);
        snapshot[0].ToolName.Should().Be("first");
        snapshot[1].ToolName.Should().Be("second");
        snapshot[2].ToolName.Should().Be("third");
    }

    // ClearSessions

    [Test]
    public void ClearSessions_PreservesInvocationLog()
    {
        // The agent report aggregates across the whole application session, so
        // ClearSessions must drop the SessionId-keyed map without touching the
        // invocation queue.
        _monitor.RecordInvocation(BuildRecord("first"));
        _monitor.RecordInvocation(BuildRecord("second"));

        _monitor.ClearSessions();

        _monitor.Invocations.Should().HaveCount(2);
    }

    [Test]
    public void ClearSessions_OnEmptyMonitorDoesNotThrow()
    {
        var act = () => _monitor.ClearSessions();
        act.Should().NotThrow();
    }

    private static ToolInvocationRecord BuildRecord(string toolName)
    {
        return new ToolInvocationRecord(
            TimestampUtc: DateTimeOffset.UtcNow,
            SessionId: "session-1",
            ClientName: "test",
            ClientVersion: "1.0",
            ToolName: toolName,
            Success: true,
            ErrorMessage: "",
            DurationMilliseconds: 0,
            ArgPayloadBytes: 0,
            ResultPayloadBytes: 0,
            ProxyClient: false,
            CacheMiss: false);
    }
}
