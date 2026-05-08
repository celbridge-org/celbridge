using System.Threading.Tasks;
using Celbridge.Server.Services;

namespace Celbridge.Tests.Server;

[TestFixture]
public class AgentTelemetryTests
{
    private AgentTelemetry _telemetry = null!;

    [SetUp]
    public void SetUp()
    {
        _telemetry = new AgentTelemetry();
    }

    // IsBootstrapTool

    [Test]
    public void IsBootstrapTool_ReturnsTrueForGuidesTriad()
    {
        _telemetry.IsBootstrapTool("guides_list").Should().BeTrue();
        _telemetry.IsBootstrapTool("guides_read").Should().BeTrue();
        _telemetry.IsBootstrapTool("guides_search").Should().BeTrue();
    }

    [Test]
    public void IsBootstrapTool_ReturnsFalseForNonBootstrapTools()
    {
        _telemetry.IsBootstrapTool("file_read").Should().BeFalse();
        _telemetry.IsBootstrapTool("app_get_state").Should().BeFalse();
        _telemetry.IsBootstrapTool("guides_unknown").Should().BeFalse();
        _telemetry.IsBootstrapTool("").Should().BeFalse();
    }

    // AgentSessionState — orientation

    [Test]
    public void OrientationRead_DefaultsToFalse()
    {
        var state = new AgentSessionState("session-1");
        state.OrientationRead.Should().BeFalse();
    }

    [Test]
    public void MarkGuideRead_AgentInstructions_FlipsOrientation()
    {
        var state = new AgentSessionState("session-1");
        state.MarkGuideRead("agent_instructions");
        state.OrientationRead.Should().BeTrue();
    }

    [Test]
    public void MarkGuideRead_OtherGuide_DoesNotFlipOrientation()
    {
        var state = new AgentSessionState("session-1");
        state.MarkGuideRead("resource_keys");
        state.OrientationRead.Should().BeFalse();
    }

    [Test]
    public void MarkGuideRead_RecordsNameInSessionSet()
    {
        var state = new AgentSessionState("session-1");
        state.MarkGuideRead("file_grep");
        state.WasGuideRead("file_grep").Should().BeTrue();
        state.WasGuideRead("file_read").Should().BeFalse();
    }

    [Test]
    public void MarkGuideRead_IsIdempotent()
    {
        var state = new AgentSessionState("session-1");
        state.MarkGuideRead("file_grep");
        state.MarkGuideRead("file_grep");
        state.WasGuideRead("file_grep").Should().BeTrue();
    }

    [Test]
    public void MarkGuideRead_IsCaseSensitive()
    {
        // guideName equality uses Ordinal comparison; "Agent_Instructions"
        // does not flip orientation. The exact casing of "agent_instructions"
        // is the contract; the gate's unlock command spells it that way.
        var state = new AgentSessionState("session-1");
        state.MarkGuideRead("Agent_Instructions");
        state.OrientationRead.Should().BeFalse();
    }

    // AgentSessionState — concurrency

    [Test]
    public async Task MarkGuideRead_ConcurrentCallsAllRecordedAndOrientationFlips()
    {
        var state = new AgentSessionState("session-1");

        var tasks = new List<Task>();
        // Mix of agent_instructions calls (orientation-flipping) and other
        // guide names (which exercise the dictionary write path concurrently).
        for (int taskIndex = 0; taskIndex < 64; taskIndex++)
        {
            var capturedIndex = taskIndex;
            tasks.Add(Task.Run(() =>
            {
                if (capturedIndex % 4 == 0)
                {
                    state.MarkGuideRead("agent_instructions");
                }
                else
                {
                    state.MarkGuideRead($"guide_{capturedIndex}");
                }
            }));
        }
        await Task.WhenAll(tasks);

        state.OrientationRead.Should().BeTrue();
        for (int index = 0; index < 64; index++)
        {
            if (index % 4 != 0)
            {
                state.WasGuideRead($"guide_{index}").Should().BeTrue();
            }
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
            _telemetry.RecordInvocation(BuildRecord($"tool_{recordIndex}"));
        }

        var snapshot = _telemetry.Invocations;
        snapshot.Count.Should().Be(cap);
        snapshot[0].ToolName.Should().Be("tool_10");
        snapshot[^1].ToolName.Should().Be($"tool_{cap + 9}");
    }

    [Test]
    public void Invocations_ReturnsRecordsInInsertionOrder()
    {
        _telemetry.RecordInvocation(BuildRecord("first"));
        _telemetry.RecordInvocation(BuildRecord("second"));
        _telemetry.RecordInvocation(BuildRecord("third"));

        var snapshot = _telemetry.Invocations;
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
        _telemetry.RecordInvocation(BuildRecord("first"));
        _telemetry.RecordInvocation(BuildRecord("second"));

        _telemetry.ClearSessions();

        _telemetry.Invocations.Should().HaveCount(2);
    }

    [Test]
    public void ClearSessions_OnEmptyTelemetryDoesNotThrow()
    {
        var act = () => _telemetry.ClearSessions();
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
