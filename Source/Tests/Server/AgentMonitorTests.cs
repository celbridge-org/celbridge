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

    // GetOrCreateSession — session-id keyed dedup

    [Test]
    public void GetOrCreateSession_SameSessionId_ReturnsSameState()
    {
        var first = _monitor.GetOrCreateSession("session-A", "");
        var second = _monitor.GetOrCreateSession("session-A", "");

        first.Should().NotBeNull();
        second.Should().BeSameAs(first);
        first!.SessionId.Should().Be("session-A");
    }

    [Test]
    public void GetOrCreateSession_DifferentSessionIds_ReturnDistinctStates()
    {
        var first = _monitor.GetOrCreateSession("session-A", "");
        var second = _monitor.GetOrCreateSession("session-B", "");

        first.Should().NotBeSameAs(second);
        first!.SessionId.Should().Be("session-A");
        second!.SessionId.Should().Be("session-B");
    }

    [Test]
    public void GetOrCreateSession_NoSessionId_ReturnsNull()
    {
        var state = _monitor.GetOrCreateSession("", "");

        state.Should().BeNull();
    }

    [Test]
    public void GetOrCreateSession_ProxyClientName_FlagsSessionAsProxy()
    {
        var state = _monitor.GetOrCreateSession("session-A", AgentMonitor.ProxyClientName);

        state!.IsProxyClient.Should().BeTrue();
    }

    [Test]
    public void GetOrCreateSession_NonProxyClientName_DoesNotFlagSessionAsProxy()
    {
        var state = _monitor.GetOrCreateSession("session-A", "claude-code");

        state!.IsProxyClient.Should().BeFalse();
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

    // FormatResponseBlocks — diagnostic-column rendering

    [Test]
    public void FormatResponseBlocks_NoAttachments_ReturnsResultSentinelOnly()
    {
        AgentMonitor.FormatResponseBlocks(Array.Empty<string>()).Should().Be("result");
    }

    [Test]
    public void FormatResponseBlocks_WithAttachments_JoinsBeforeResultSentinel()
    {
        var attached = new[] { "app_state", "document_state", "agent_instructions", "file", "file_read" };

        AgentMonitor.FormatResponseBlocks(attached)
            .Should().Be("app_state; document_state; agent_instructions; file; file_read; result");
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
            ResponseBlocks: "result");
    }
}
