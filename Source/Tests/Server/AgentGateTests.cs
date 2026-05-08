using System.Text.Json;
using Celbridge.Server.Services;

namespace Celbridge.Tests.Server;

[TestFixture]
public class AgentGateTests
{
    // ParseRequestedGuideNames — JSON array of strings

    [Test]
    public void ParseRequestedGuideNames_FromJsonArrayElement()
    {
        var element = ParseElement("[\"agent_instructions\",\"file_grep\"]");
        var names = AgentGate.ParseRequestedGuideNames(element);
        names.Should().Equal("agent_instructions", "file_grep");
    }

    [Test]
    public void ParseRequestedGuideNames_FromJsonStringWrappingArray()
    {
        // Some MCP clients send `names` as a quoted JSON string (the guides_read
        // tool itself parses string-typed args as JSON internally). The gate
        // mirrors that so the cache-miss tracking matches what the tool sees.
        var element = ParseElement("\"[\\\"agent_instructions\\\"]\"");
        var names = AgentGate.ParseRequestedGuideNames(element);
        names.Should().Equal("agent_instructions");
    }

    [Test]
    public void ParseRequestedGuideNames_EmptyArrayReturnsEmptyList()
    {
        var element = ParseElement("[]");
        var names = AgentGate.ParseRequestedGuideNames(element);
        names.Should().BeEmpty();
    }

    [Test]
    public void ParseRequestedGuideNames_MalformedJsonReturnsEmptyList()
    {
        var element = ParseElement("\"[unclosed\"");
        var names = AgentGate.ParseRequestedGuideNames(element);
        names.Should().BeEmpty();
    }

    [Test]
    public void ParseRequestedGuideNames_DropsEmptyStringEntries()
    {
        var element = ParseElement("[\"\",\"agent_instructions\",\"\"]");
        var names = AgentGate.ParseRequestedGuideNames(element);
        names.Should().Equal("agent_instructions");
    }

    // ApplyGuidesReadSideEffects — full path through to AgentSessionState

    [Test]
    public void ApplyGuidesReadSideEffects_AgentInstructionsFlipsOrientation()
    {
        var telemetry = new AgentTelemetry();
        var session = new AgentSessionState("session-1");
        var arguments = BuildArguments("[\"agent_instructions\"]");

        AgentGate.ApplyGuidesReadSideEffects(telemetry, session, arguments);

        session.OrientationRead.Should().BeTrue();
        session.WasGuideRead("agent_instructions").Should().BeTrue();
    }

    [Test]
    public void ApplyGuidesReadSideEffects_RecordsNonOrientationGuide()
    {
        var telemetry = new AgentTelemetry();
        var session = new AgentSessionState("session-1");
        var arguments = BuildArguments("[\"file_grep\",\"resource_keys\"]");

        AgentGate.ApplyGuidesReadSideEffects(telemetry, session, arguments);

        session.OrientationRead.Should().BeFalse();
        session.WasGuideRead("file_grep").Should().BeTrue();
        session.WasGuideRead("resource_keys").Should().BeTrue();
    }

    [Test]
    public void ApplyGuidesReadSideEffects_NullArgumentsIsNoOp()
    {
        var telemetry = new AgentTelemetry();
        var session = new AgentSessionState("session-1");

        AgentGate.ApplyGuidesReadSideEffects(telemetry, session, null);

        session.OrientationRead.Should().BeFalse();
    }

    [Test]
    public void ApplyGuidesReadSideEffects_MissingNamesKeyIsNoOp()
    {
        var telemetry = new AgentTelemetry();
        var session = new AgentSessionState("session-1");
        var arguments = new Dictionary<string, JsonElement>
        {
            ["other"] = ParseElement("\"value\"")
        };

        AgentGate.ApplyGuidesReadSideEffects(telemetry, session, arguments);

        session.OrientationRead.Should().BeFalse();
    }

    [Test]
    public void ApplyGuidesReadSideEffects_MalformedNamesIsNoOp()
    {
        // Bad JSON in `names` is the case the tool itself rejects with a
        // BootstrapToolError — the side effects must not record anything when
        // the inner handler returns failure. The gate already gates this on
        // the tool result; this test pins the inner-helper behaviour.
        var telemetry = new AgentTelemetry();
        var session = new AgentSessionState("session-1");
        var arguments = BuildArguments("\"[unclosed\"");

        AgentGate.ApplyGuidesReadSideEffects(telemetry, session, arguments);

        session.OrientationRead.Should().BeFalse();
        session.WasGuideRead("agent_instructions").Should().BeFalse();
    }

    private static JsonElement ParseElement(string json)
    {
        return JsonDocument.Parse(json).RootElement;
    }

    private static IDictionary<string, JsonElement> BuildArguments(string namesJson)
    {
        return new Dictionary<string, JsonElement>
        {
            ["names"] = ParseElement(namesJson)
        };
    }
}
