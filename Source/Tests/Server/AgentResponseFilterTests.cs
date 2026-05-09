using System.Text.Json;
using System.Threading.Tasks;
using Celbridge.Server.Services;
using Celbridge.Tools;
using ModelContextProtocol.Protocol;

namespace Celbridge.Tests.Server;

[TestFixture]
public class AgentResponseFilterTests
{
    private const string OrientationBody = "# Agent instructions body";
    private const string FileNamespaceBody = "# File namespace body";
    private const string FileReadBody = "# file_read tool body";
    private const string FileGrepBody = "# file_grep tool body";
    private const string AppNamespaceBody = "# App namespace body";
    private const string AppGetStateBody = "# app_get_state tool body";

    private AgentMonitor _monitor = null!;
    private FakeGuides _guides = null!;
    private AgentResponseFilter _filter = null!;

    [SetUp]
    public void SetUp()
    {
        _monitor = new AgentMonitor();
        _guides = new FakeGuides
        {
            Bodies =
            {
                ["agent_instructions"] = OrientationBody,
                ["file"] = FileNamespaceBody,
                ["file_read"] = FileReadBody,
                ["file_grep"] = FileGrepBody,
                ["app"] = AppNamespaceBody,
                ["app_get_state"] = AppGetStateBody,
            }
        };
        _filter = new AgentResponseFilter(_monitor, _guides);
    }

    // ApplyAutoAttach — first-use behaviour

    [Test]
    public void ApplyAutoAttach_FirstCall_AttachesOrientationNamespaceAndToolGuide()
    {
        var session = new AgentSessionState("session-1");
        var result = BuildSuccess("file_read result");

        var attached = _filter.ApplyAutoAttach(result, session, "file_read");

        var blocks = attached.Content!;
        blocks.Should().HaveCount(4);
        TextAt(blocks, 0).Should().Be(OrientationBody);
        TextAt(blocks, 1).Should().Be(FileNamespaceBody);
        TextAt(blocks, 2).Should().Be(FileReadBody);
        TextAt(blocks, 3).Should().Be("file_read result");
    }

    [Test]
    public void ApplyAutoAttach_RepeatCall_ReturnsBareResult()
    {
        var session = new AgentSessionState("session-1");

        _filter.ApplyAutoAttach(BuildSuccess("first"), session, "file_read");
        var second = _filter.ApplyAutoAttach(BuildSuccess("second"), session, "file_read");

        second.Content.Should().HaveCount(1);
        TextAt(second.Content!, 0).Should().Be("second");
    }

    [Test]
    public void ApplyAutoAttach_DifferentToolSameNamespace_AttachesOnlyPerToolGuide()
    {
        var session = new AgentSessionState("session-1");

        _filter.ApplyAutoAttach(BuildSuccess("first"), session, "file_read");
        var second = _filter.ApplyAutoAttach(BuildSuccess("second"), session, "file_grep");

        second.Content.Should().HaveCount(2);
        TextAt(second.Content!, 0).Should().Be(FileGrepBody);
        TextAt(second.Content!, 1).Should().Be("second");
    }

    [Test]
    public void ApplyAutoAttach_NewNamespace_AttachesNamespaceAndPerToolGuide_ButNotOrientation()
    {
        var session = new AgentSessionState("session-1");

        _filter.ApplyAutoAttach(BuildSuccess("first"), session, "file_read");
        var second = _filter.ApplyAutoAttach(BuildSuccess("second"), session, "app_get_state");

        second.Content.Should().HaveCount(3);
        TextAt(second.Content!, 0).Should().Be(AppNamespaceBody);
        TextAt(second.Content!, 1).Should().Be(AppGetStateBody);
        TextAt(second.Content!, 2).Should().Be("second");
    }

    // Proxy connections

    [Test]
    public void ApplyAutoAttach_ProxyConnection_ReturnsBareResult()
    {
        var session = new AgentSessionState("session-1") { IsProxyClient = true };
        var result = BuildSuccess("proxy result");

        var attached = _filter.ApplyAutoAttach(result, session, "file_read");

        attached.Content.Should().HaveCount(1);
        TextAt(attached.Content!, 0).Should().Be("proxy result");
        // Proxy bypass should not consume the per-session served-guides budget.
        session.WasGuideRead("agent_instructions").Should().BeFalse();
        session.WasGuideRead("file_read").Should().BeFalse();
    }

    // Errors

    [Test]
    public void ApplyAutoAttach_ErrorResultOnFirstUse_PrependsGuidesAndPreservesIsError()
    {
        var session = new AgentSessionState("session-1");
        var result = BuildError("file_read failed");

        var attached = _filter.ApplyAutoAttach(result, session, "file_read");

        attached.IsError.Should().BeTrue();
        attached.Content.Should().HaveCount(4);
        TextAt(attached.Content!, 0).Should().Be(OrientationBody);
        TextAt(attached.Content!, 1).Should().Be(FileNamespaceBody);
        TextAt(attached.Content!, 2).Should().Be(FileReadBody);
        TextAt(attached.Content!, 3).Should().Be("file_read failed");
    }

    // Race: parallel first calls in the same namespace

    [Test]
    public async Task ApplyAutoAttach_ParallelFirstCallsSameNamespace_AttachNamespaceExactlyOnce()
    {
        var session = new AgentSessionState("session-1");

        var readTask = Task.Run(() => _filter.ApplyAutoAttach(BuildSuccess("read"), session, "file_read"));
        var grepTask = Task.Run(() => _filter.ApplyAutoAttach(BuildSuccess("grep"), session, "file_grep"));

        await Task.WhenAll(readTask, grepTask);

        var readResult = readTask.Result;
        var grepResult = grepTask.Result;

        var readBodies = ExtractBodies(readResult);
        var grepBodies = ExtractBodies(grepResult);

        // Both calls together should attach the namespace guide exactly once,
        // exactly one orientation guide, and each per-tool guide once.
        var combined = readBodies.Concat(grepBodies).ToList();
        combined.Count(body => body == OrientationBody).Should().Be(1);
        combined.Count(body => body == FileNamespaceBody).Should().Be(1);
        combined.Count(body => body == FileReadBody).Should().Be(1);
        combined.Count(body => body == FileGrepBody).Should().Be(1);

        // Each call should still receive its own per-tool guide.
        readBodies.Should().Contain(FileReadBody);
        grepBodies.Should().Contain(FileGrepBody);
    }

    // Missing guide bodies (defence-in-depth)

    [Test]
    public void ApplyAutoAttach_UnknownToolName_DoesNotPrepend()
    {
        var session = new AgentSessionState("session-1");
        // No matching per-tool entry in FakeGuides; namespace and orientation
        // still attach because they exist.
        var attached = _filter.ApplyAutoAttach(BuildSuccess("payload"), session, "file_unknown");

        attached.Content.Should().HaveCount(3);
        TextAt(attached.Content!, 0).Should().Be(OrientationBody);
        TextAt(attached.Content!, 1).Should().Be(FileNamespaceBody);
        TextAt(attached.Content!, 2).Should().Be("payload");
        // The TryMarkServed slot is consumed even when no body was attached, so
        // a follow-up call doesn't prepend a phantom block either.
        session.WasGuideRead("file_unknown").Should().BeTrue();
    }

    // ParseRequestedGuideNames — JSON array of strings

    [Test]
    public void ParseRequestedGuideNames_FromJsonArrayElement()
    {
        var element = ParseElement("[\"agent_instructions\",\"file_grep\"]");
        var names = AgentResponseFilter.ParseRequestedGuideNames(element);
        names.Should().Equal("agent_instructions", "file_grep");
    }

    [Test]
    public void ParseRequestedGuideNames_FromJsonStringWrappingArray()
    {
        // Some MCP clients send `names` as a quoted JSON string (the guides_read
        // tool itself parses string-typed args as JSON internally). The filter
        // mirrors that so the served-guide tracking matches what the tool sees.
        var element = ParseElement("\"[\\\"agent_instructions\\\"]\"");
        var names = AgentResponseFilter.ParseRequestedGuideNames(element);
        names.Should().Equal("agent_instructions");
    }

    [Test]
    public void ParseRequestedGuideNames_EmptyArrayReturnsEmptyList()
    {
        var element = ParseElement("[]");
        var names = AgentResponseFilter.ParseRequestedGuideNames(element);
        names.Should().BeEmpty();
    }

    [Test]
    public void ParseRequestedGuideNames_MalformedJsonReturnsEmptyList()
    {
        var element = ParseElement("\"[unclosed\"");
        var names = AgentResponseFilter.ParseRequestedGuideNames(element);
        names.Should().BeEmpty();
    }

    [Test]
    public void ParseRequestedGuideNames_DropsEmptyStringEntries()
    {
        var element = ParseElement("[\"\",\"agent_instructions\",\"\"]");
        var names = AgentResponseFilter.ParseRequestedGuideNames(element);
        names.Should().Equal("agent_instructions");
    }

    // ApplyGuidesReadSideEffects — full path through to AgentSessionState

    [Test]
    public void ApplyGuidesReadSideEffects_RecordsRequestedGuides()
    {
        var monitor = new AgentMonitor();
        var session = new AgentSessionState("session-1");
        var arguments = BuildArguments("[\"file_grep\",\"resource_keys\"]");

        AgentResponseFilter.ApplyGuidesReadSideEffects(monitor, session, arguments);

        session.WasGuideRead("file_grep").Should().BeTrue();
        session.WasGuideRead("resource_keys").Should().BeTrue();
    }

    [Test]
    public void ApplyGuidesReadSideEffects_NullArgumentsIsNoOp()
    {
        var monitor = new AgentMonitor();
        var session = new AgentSessionState("session-1");

        AgentResponseFilter.ApplyGuidesReadSideEffects(monitor, session, null);

        session.WasGuideRead("agent_instructions").Should().BeFalse();
    }

    [Test]
    public void ApplyGuidesReadSideEffects_MissingNamesKeyIsNoOp()
    {
        var monitor = new AgentMonitor();
        var session = new AgentSessionState("session-1");
        var arguments = new Dictionary<string, JsonElement>
        {
            ["other"] = ParseElement("\"value\"")
        };

        AgentResponseFilter.ApplyGuidesReadSideEffects(monitor, session, arguments);

        session.WasGuideRead("agent_instructions").Should().BeFalse();
    }

    [Test]
    public void ApplyGuidesReadSideEffects_MalformedNamesIsNoOp()
    {
        // Bad JSON in `names` is the case the tool itself rejects with an
        // error result; the side effects must not record anything when the
        // inner handler returns failure. The filter already gates this on the
        // tool result; this test pins the inner-helper behaviour.
        var monitor = new AgentMonitor();
        var session = new AgentSessionState("session-1");
        var arguments = BuildArguments("\"[unclosed\"");

        AgentResponseFilter.ApplyGuidesReadSideEffects(monitor, session, arguments);

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

    private static CallToolResult BuildSuccess(string text)
    {
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = text }],
        };
    }

    private static CallToolResult BuildError(string text)
    {
        return new CallToolResult
        {
            IsError = true,
            Content = [new TextContentBlock { Text = text }],
        };
    }

    private static string TextAt(IList<ContentBlock> blocks, int index)
    {
        var block = blocks[index];
        if (block is TextContentBlock text)
        {
            return text.Text ?? "";
        }
        return "";
    }

    private static List<string> ExtractBodies(CallToolResult result)
    {
        var bodies = new List<string>();
        if (result.Content is null)
        {
            return bodies;
        }
        foreach (var block in result.Content)
        {
            if (block is TextContentBlock text && !string.IsNullOrEmpty(text.Text))
            {
                bodies.Add(text.Text);
            }
        }
        return bodies;
    }

    /// <summary>
    /// Minimal IGuides used for filter tests. Returns a GuideEntry with the
    /// canned body for known names and null for unknown names.
    /// </summary>
    private sealed class FakeGuides : IGuides
    {
        public Dictionary<string, string> Bodies { get; } = new(StringComparer.Ordinal);

        public GuideEntry? GetByName(string name)
        {
            if (Bodies.TryGetValue(name, out var body))
            {
                return new GuideEntry(
                    Name: name,
                    Kind: GuideKind.Tool,
                    Body: body,
                    PythonInvocation: null,
                    JavaScriptInvocation: null);
            }
            return null;
        }
    }
}
