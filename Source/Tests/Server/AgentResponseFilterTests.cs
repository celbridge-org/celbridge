using System.Text.Json;
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
    private const string ResourceKeysBody = "# resource_keys concept body";
    private const string RegexSyntaxBody = "# regex_syntax concept body";
    private const string TroubleshootResourceKeyBody = "# troubleshoot_resource_key body";

    // First-call attachments always include two session-state blocks
    // (app + open documents) ahead of the guide bodies.
    private const int SessionStateBlockCount = 2;
    private const int AppStateBlockIndex = 0;
    private const int DocumentStateBlockIndex = 1;

    private AgentMonitor _monitor = null!;
    private FakeGuides _guides = null!;
    private FakeAppStateProvider _appStateProvider = null!;
    private FakeDocumentStateProvider _documentStateProvider = null!;
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
                ["resource_keys"] = ResourceKeysBody,
                ["regex_syntax"] = RegexSyntaxBody,
                ["troubleshoot_resource_key"] = TroubleshootResourceKeyBody,
            },
            RelatedByTool =
            {
                ["file_read"] = new[] { "resource_keys" },
                ["file_grep"] = new[] { "resource_keys", "regex_syntax" },
                ["app_get_state"] = Array.Empty<string>(),
            }
        };
        _appStateProvider = new FakeAppStateProvider();
        _documentStateProvider = new FakeDocumentStateProvider();
        _filter = new AgentResponseFilter(_monitor, _guides, _appStateProvider, _documentStateProvider);
    }

    // ApplyAutoAttachAsync — first-use behaviour

    [Test]
    public async Task ApplyAutoAttachAsync_FirstCall_AttachesSessionStateOrientationNamespacePerToolAndRelated()
    {
        var session = new AgentSessionState("session-1");
        var result = BuildSuccess("file_read result");

        var attached = await _filter.ApplyAutoAttachAsync(result, session, "file_read");

        var blocks = attached.Result.Content!;
        // 2 session-state blocks + orientation + namespace + per-tool + related + result
        blocks.Should().HaveCount(SessionStateBlockCount + 5);
        TextAt(blocks, AppStateBlockIndex).Should().Contain("# App state");
        TextAt(blocks, DocumentStateBlockIndex).Should().Contain("# Open documents");
        TextAt(blocks, SessionStateBlockCount + 0).Should().Be(OrientationBody);
        TextAt(blocks, SessionStateBlockCount + 1).Should().Be(FileNamespaceBody);
        TextAt(blocks, SessionStateBlockCount + 2).Should().Be(FileReadBody);
        TextAt(blocks, SessionStateBlockCount + 3).Should().Be(ResourceKeysBody);
        TextAt(blocks, SessionStateBlockCount + 4).Should().Be("file_read result");
    }

    [Test]
    public async Task ApplyAutoAttachAsync_RepeatCall_ReturnsBareResult()
    {
        var session = new AgentSessionState("session-1");

        await _filter.ApplyAutoAttachAsync(BuildSuccess("first"), session, "file_read");
        var second = await _filter.ApplyAutoAttachAsync(BuildSuccess("second"), session, "file_read");

        second.Result.Content.Should().HaveCount(1);
        TextAt(second.Result.Content!, 0).Should().Be("second");
    }

    [Test]
    public async Task ApplyAutoAttachAsync_DifferentToolSameNamespace_AttachesOnlyPerToolAndUnservedRelated()
    {
        var session = new AgentSessionState("session-1");

        // First call to file_read serves session state, orientation, file
        // namespace, file_read, and the file_read related concept (resource_keys).
        await _filter.ApplyAutoAttachAsync(BuildSuccess("first"), session, "file_read");

        // file_grep declares resource_keys (already served) plus regex_syntax (new).
        var second = await _filter.ApplyAutoAttachAsync(BuildSuccess("second"), session, "file_grep");

        second.Result.Content.Should().HaveCount(3);
        TextAt(second.Result.Content!, 0).Should().Be(FileGrepBody);
        TextAt(second.Result.Content!, 1).Should().Be(RegexSyntaxBody);
        TextAt(second.Result.Content!, 2).Should().Be("second");
    }

    [Test]
    public async Task ApplyAutoAttachAsync_NewNamespace_AttachesNamespaceAndPerToolGuide_ButNotOrientation()
    {
        var session = new AgentSessionState("session-1");

        await _filter.ApplyAutoAttachAsync(BuildSuccess("first"), session, "file_read");
        var second = await _filter.ApplyAutoAttachAsync(BuildSuccess("second"), session, "app_get_state");

        second.Result.Content.Should().HaveCount(3);
        TextAt(second.Result.Content!, 0).Should().Be(AppNamespaceBody);
        TextAt(second.Result.Content!, 1).Should().Be(AppGetStateBody);
        TextAt(second.Result.Content!, 2).Should().Be("second");
    }

    // Proxy connections

    [Test]
    public async Task ApplyAutoAttachAsync_ProxyConnection_ReturnsBareResult()
    {
        var session = new AgentSessionState("session-1") { IsProxyClient = true };
        var result = BuildSuccess("proxy result");

        var attached = await _filter.ApplyAutoAttachAsync(result, session, "file_read");

        attached.Result.Content.Should().HaveCount(1);
        TextAt(attached.Result.Content!, 0).Should().Be("proxy result");
        // Proxy bypass should not consume the per-session served-guides budget.
        session.WasGuideRead("agent_instructions").Should().BeFalse();
        session.WasGuideRead("file_read").Should().BeFalse();
        session.WasGuideRead(AgentResponseFilter.SessionStateMarker).Should().BeFalse();
    }

    // Errors

    [Test]
    public async Task ApplyAutoAttachAsync_ErrorResultOnFirstUse_PrependsBlocksAndPreservesIsError()
    {
        var session = new AgentSessionState("session-1");
        var result = BuildError("file_read failed");

        var attached = await _filter.ApplyAutoAttachAsync(result, session, "file_read");

        attached.Result.IsError.Should().BeTrue();
        attached.Result.Content.Should().HaveCount(SessionStateBlockCount + 5);
        TextAt(attached.Result.Content!, SessionStateBlockCount + 0).Should().Be(OrientationBody);
        TextAt(attached.Result.Content!, SessionStateBlockCount + 4).Should().Be("file_read failed");
    }

    // Race: parallel first calls in the same namespace

    [Test]
    public async Task ApplyAutoAttachAsync_ParallelFirstCallsSameNamespace_AttachNamespaceExactlyOnce()
    {
        var session = new AgentSessionState("session-1");

        var readTask = Task.Run(() => _filter.ApplyAutoAttachAsync(BuildSuccess("read"), session, "file_read"));
        var grepTask = Task.Run(() => _filter.ApplyAutoAttachAsync(BuildSuccess("grep"), session, "file_grep"));

        await Task.WhenAll(readTask, grepTask);

        var combined = ExtractBodies(readTask.Result.Result).Concat(ExtractBodies(grepTask.Result.Result)).ToList();

        // Both calls together should attach orientation, namespace, each per-tool,
        // and each related concept exactly once across the pair. Session-state
        // blocks should also each appear exactly once.
        combined.Count(body => body == OrientationBody).Should().Be(1);
        combined.Count(body => body == FileNamespaceBody).Should().Be(1);
        combined.Count(body => body == FileReadBody).Should().Be(1);
        combined.Count(body => body == FileGrepBody).Should().Be(1);
        combined.Count(body => body == ResourceKeysBody).Should().Be(1);
        combined.Count(body => body == RegexSyntaxBody).Should().Be(1);
        combined.Count(body => body.StartsWith("# App state")).Should().Be(1);
        combined.Count(body => body.StartsWith("# Open documents")).Should().Be(1);
    }

    // Related-guides attachment ordering

    [Test]
    public async Task ApplyAutoAttachAsync_RelatedGuidesArriveAfterPerToolInDeclarationOrder()
    {
        var session = new AgentSessionState("session-1");

        // Burn the session state, orientation, and namespace slots so the assertion
        // focuses on the per-tool + related ordering.
        await _filter.ApplyAutoAttachAsync(BuildSuccess("warmup"), session, "file_read");

        var attached = await _filter.ApplyAutoAttachAsync(BuildSuccess("grep result"), session, "file_grep");

        // file_grep declares ["resource_keys", "regex_syntax"]; resource_keys
        // already served on the warmup call. So expect: file_grep body, then
        // regex_syntax, then the result. Per-tool stays before related.
        attached.Result.Content.Should().HaveCount(3);
        TextAt(attached.Result.Content!, 0).Should().Be(FileGrepBody);
        TextAt(attached.Result.Content!, 1).Should().Be(RegexSyntaxBody);
        TextAt(attached.Result.Content!, 2).Should().Be("grep result");
    }

    // Session-state attach pipeline

    [Test]
    public async Task ApplyAutoAttachAsync_SessionState_AttachesAppAndDocumentBlocksOnFirstCall()
    {
        _appStateProvider.State = new AppStateResult(
            Version: "9.9.9-fake",
            IsLoaded: true,
            ProjectName: "ProbeProject",
            FeatureFlags: new Dictionary<string, bool>(),
            FocusedPanel: "Documents",
            LayoutMode: new LayoutModeInfo(true, false, true, false),
            SpotlightLandmarks: new List<string>());
        _documentStateProvider.Result = new DocumentStateResult(
            ActiveDocument: "/Notes/README.md",
            SectionCount: 1,
            OpenDocuments: new List<OpenDocumentEntry>
            {
                new OpenDocumentEntry("/Notes/README.md", 0, 0, true, "markdown"),
            });

        var session = new AgentSessionState("session-1");
        var attached = await _filter.ApplyAutoAttachAsync(BuildSuccess("payload"), session, "file_read");

        TextAt(attached.Result.Content!, AppStateBlockIndex).Should().Contain("\"version\": \"9.9.9-fake\"");
        TextAt(attached.Result.Content!, AppStateBlockIndex).Should().Contain("\"projectName\": \"ProbeProject\"");
        TextAt(attached.Result.Content!, DocumentStateBlockIndex).Should().Contain("\"activeDocument\": \"/Notes/README.md\"");
        TextAt(attached.Result.Content!, DocumentStateBlockIndex).Should().Contain("# Open documents");
    }

    [Test]
    public async Task ApplyAutoAttachAsync_SessionState_DoesNotReAttachOnSecondCall()
    {
        var session = new AgentSessionState("session-1");
        await _filter.ApplyAutoAttachAsync(BuildSuccess("first"), session, "file_read");

        var second = await _filter.ApplyAutoAttachAsync(BuildSuccess("second"), session, "file_read");

        // No prepended blocks on the second call (everything's already served).
        second.Result.Content.Should().HaveCount(1);
        TextAt(second.Result.Content!, 0).Should().Be("second");
    }

    [Test]
    public async Task ApplyAutoAttachAsync_SessionState_DocumentProviderFailureSkipsDocumentBlockOnly()
    {
        _documentStateProvider.Result = Result<DocumentStateResult>.Fail("document state unavailable");

        var session = new AgentSessionState("session-1");
        var attached = await _filter.ApplyAutoAttachAsync(BuildSuccess("payload"), session, "file_read");

        // App state still attaches; document state is omitted because the
        // provider failed. Slot count drops by one.
        var bodies = ExtractBodies(attached.Result);
        bodies.Should().Contain(b => b.StartsWith("# App state"));
        bodies.Should().NotContain(b => b.StartsWith("# Open documents"));
    }

    // Troubleshooter Meta pipeline

    [Test]
    public async Task ApplyAutoAttachAsync_TroubleshooterMeta_AttachesTroubleshooterAndClearsMeta()
    {
        var session = new AgentSessionState("session-1");

        // Burn session state/orientation/namespace/per-tool/related so this test
        // focuses on the troubleshooter slot.
        await _filter.ApplyAutoAttachAsync(BuildSuccess("warmup"), session, "file_read");

        var helperResult = ToolResponse.InvalidResourceKey("Bad\\Key");
        var attached = await _filter.ApplyAutoAttachAsync(helperResult, session, "file_read");

        attached.Result.IsError.Should().BeTrue();
        // Expect: troubleshoot_resource_key body, then the original error
        // text. The Meta hint must not survive into the response.
        attached.Result.Content.Should().HaveCount(2);
        TextAt(attached.Result.Content!, 0).Should().Be(TroubleshootResourceKeyBody);
        TextAt(attached.Result.Content!, 1).Should().Be("Invalid resource key: 'Bad\\Key'.");
        attached.Result.Meta?.ContainsKey(ToolResponse.TroubleshooterMetaKey).Should().NotBe(true);
    }

    [Test]
    public async Task ApplyAutoAttachAsync_TroubleshooterRepeatCall_DoesNotReAttach()
    {
        var session = new AgentSessionState("session-1");

        await _filter.ApplyAutoAttachAsync(BuildSuccess("warmup"), session, "file_read");

        await _filter.ApplyAutoAttachAsync(ToolResponse.InvalidResourceKey("Bad\\Key"), session, "file_read");
        var second = await _filter.ApplyAutoAttachAsync(ToolResponse.InvalidResourceKey("Other\\Key"), session, "file_read");

        // Meta hint still gets cleared on the second call, so the response
        // contains only the (capped) error text and no leaked Meta entry.
        second.Result.IsError.Should().BeTrue();
        second.Result.Content.Should().HaveCount(1);
        TextAt(second.Result.Content!, 0).Should().Be("Invalid resource key: 'Other\\Key'.");
        second.Result.Meta?.ContainsKey(ToolResponse.TroubleshooterMetaKey).Should().NotBe(true);
    }

    // Missing guide bodies (defence-in-depth)

    [Test]
    public async Task ApplyAutoAttachAsync_UnknownToolName_DoesNotPrependPerToolBody()
    {
        var session = new AgentSessionState("session-1");
        // No matching per-tool entry in FakeGuides; namespace, orientation, and
        // session state still attach because they exist.
        var attached = await _filter.ApplyAutoAttachAsync(BuildSuccess("payload"), session, "file_unknown");

        attached.Result.Content.Should().HaveCount(SessionStateBlockCount + 3);
        TextAt(attached.Result.Content!, SessionStateBlockCount + 0).Should().Be(OrientationBody);
        TextAt(attached.Result.Content!, SessionStateBlockCount + 1).Should().Be(FileNamespaceBody);
        TextAt(attached.Result.Content!, SessionStateBlockCount + 2).Should().Be("payload");
        // The TryMarkServed slot is consumed even when no body was attached, so
        // a follow-up call doesn't prepend a phantom block either.
        session.WasGuideRead("file_unknown").Should().BeTrue();
    }

    // AutoAttachOutcome — AttachedNames list for the diagnostic column

    [Test]
    public async Task ApplyAutoAttachAsync_FirstCall_ReturnsAttachedNamesInBroadestFirstOrder()
    {
        var session = new AgentSessionState("session-1");

        var attached = await _filter.ApplyAutoAttachAsync(BuildSuccess("payload"), session, "file_read");

        attached.AttachedNames.Should().Equal(
            AgentResponseFilter.AppStateBlockName,
            AgentResponseFilter.DocumentStateBlockName,
            "agent_instructions",
            "file",
            "file_read",
            "resource_keys");
    }

    [Test]
    public async Task ApplyAutoAttachAsync_RepeatCall_ReturnsEmptyAttachedNames()
    {
        var session = new AgentSessionState("session-1");
        await _filter.ApplyAutoAttachAsync(BuildSuccess("first"), session, "file_read");

        var second = await _filter.ApplyAutoAttachAsync(BuildSuccess("second"), session, "file_read");

        second.AttachedNames.Should().BeEmpty();
    }

    [Test]
    public async Task ApplyAutoAttachAsync_ProxyConnection_ReturnsEmptyAttachedNames()
    {
        var session = new AgentSessionState("session-1") { IsProxyClient = true };

        var attached = await _filter.ApplyAutoAttachAsync(BuildSuccess("payload"), session, "file_read");

        attached.AttachedNames.Should().BeEmpty();
    }

    [Test]
    public async Task ApplyAutoAttachAsync_DocumentProviderFailure_OmitsDocumentStateFromAttachedNames()
    {
        _documentStateProvider.Result = Result<DocumentStateResult>.Fail("document state unavailable");
        var session = new AgentSessionState("session-1");

        var attached = await _filter.ApplyAutoAttachAsync(BuildSuccess("payload"), session, "file_read");

        attached.AttachedNames.Should().Contain(AgentResponseFilter.AppStateBlockName);
        attached.AttachedNames.Should().NotContain(AgentResponseFilter.DocumentStateBlockName);
    }

    // Candidate list construction (pure)

    [Test]
    public void BuildCandidateList_OrderIsBroadestFirst()
    {
        var candidates = _filter.BuildCandidateList("file_grep", new[] { "troubleshoot_resource_key" });

        candidates.Should().Equal(
            "agent_instructions",
            "file",
            "file_grep",
            "resource_keys",
            "regex_syntax",
            "troubleshoot_resource_key");
    }

    [Test]
    public void BuildCandidateList_NoNamespace_OmitsNamespaceSlot()
    {
        var candidates = _filter.BuildCandidateList("standalone", Array.Empty<string>());

        candidates.Should().Equal("agent_instructions", "standalone");
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
    /// canned body for known names and null for unknown names; carries a
    /// per-tool related-guides map keyed on alias name.
    /// </summary>
    private sealed class FakeGuides : IGuides
    {
        public Dictionary<string, string> Bodies { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, IReadOnlyList<string>> RelatedByTool { get; } = new(StringComparer.Ordinal);

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

        public IReadOnlyList<string> GetRelatedGuides(string toolAliasName)
        {
            if (RelatedByTool.TryGetValue(toolAliasName, out var list))
            {
                return list;
            }
            return Array.Empty<string>();
        }
    }

    private sealed class FakeAppStateProvider : IAppStateProvider
    {
        public AppStateResult State { get; set; } = new AppStateResult(
            Version: "1.0.0-test",
            IsLoaded: true,
            ProjectName: "TestProject",
            FeatureFlags: new Dictionary<string, bool>(),
            FocusedPanel: "None",
            LayoutMode: new LayoutModeInfo(true, true, false, false),
            SpotlightLandmarks: new List<string>());

        public AppStateResult GetState() => State;
    }

    private sealed class FakeDocumentStateProvider : IDocumentStateProvider
    {
        public Result<DocumentStateResult> Result { get; set; } =
            new DocumentStateResult("", 1, new List<OpenDocumentEntry>());

        public Task<Result<DocumentStateResult>> GetStateAsync() => Task.FromResult(Result);
    }
}
