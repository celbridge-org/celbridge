using System.Text.Json;
using Celbridge.Commands;
using Celbridge.FileSystem.Services;
using Celbridge.Tests.Migration.TestHelpers;
using Celbridge.WebHost;
using Celbridge.WebHost.Services;

namespace Celbridge.Tests.WebHost;

[TestFixture]
public partial class DocumentWebViewToolBridgeTests
{
    private ICommandService _commandService = null!;
    private ILogger<DocumentWebViewToolBridge> _logger = null!;
    private ILocalFileSystem _fileSystem = null!;
    private DocumentWebViewToolBridge _bridge = null!;
    private ResourceKey _resource;

    [SetUp]
    public void SetUp()
    {
        _commandService = Substitute.For<ICommandService>();
        _logger = Substitute.For<ILogger<DocumentWebViewToolBridge>>();
        _fileSystem = new LocalFileSystem(MigrationTestHelper.CreateMockLogger<LocalFileSystem>());

        ResourceKey.TryCreate("docs/readme.md", out _resource).Should().BeTrue();

        // The bridge calls IGetWebViewToolSupportCommand through the command queue
        // to build the diagnostic on a missing-registration error. Stub it to return
        // a recognizable sentinel so we can verify the bridge surfaces the reason
        // verbatim. The diagnostic text itself is tested in WebViewServiceSupportTests.
        StubSupport(new WebViewToolSupport(
            IsSupported: false,
            Reason: $"UNSUPPORTED:{_resource}"));

        _bridge = new DocumentWebViewToolBridge(_commandService, _logger, _fileSystem);
    }

    private void StubSupport(WebViewToolSupport support)
    {
        _commandService
            .ExecuteAsync<IGetWebViewToolSupportCommand, WebViewToolSupport>(
                Arg.Any<Action<IGetWebViewToolSupportCommand>?>(),
                Arg.Any<string>(),
                Arg.Any<int>())
            .Returns(Result<WebViewToolSupport>.Ok(support));
    }

    [Test]
    public async Task EvalAsync_NoRegistration_SurfacesUnsupportedReason()
    {
        var result = await _bridge.EvalAsync(_resource, "1 + 1");

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Be($"UNSUPPORTED:{_resource}");
    }

    [Test]
    public async Task ReloadAsync_NoRegistration_SurfacesUnsupportedReason()
    {
        var result = await _bridge.ReloadAsync(_resource, clearCache: false);

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Be($"UNSUPPORTED:{_resource}");
    }

    [Test]
    public async Task EvalAsync_NoRegistration_SupportedResource_FallsBackToGenericMessage()
    {
        // When the service reports the resource is supported, the failure is
        // purely that no WebView is registered, so the bridge surfaces its
        // own message.
        StubSupport(new WebViewToolSupport(IsSupported: true, Reason: null));

        var result = await _bridge.EvalAsync(_resource, "1 + 1");

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain(_resource.ToString());
        result.FirstErrorMessage.Should().Contain("No WebView is registered");
    }

    [Test]
    public async Task EvalAsync_AfterRegistrationAndContentReady_ReturnsDelegateResult()
    {
        _bridge.Register(
            _resource,
            evalAsync: PageWithoutFrames(expression => Task.FromResult($"\"echo:{expression}\"")),
            reloadAsync: _ => Task.CompletedTask);
        _bridge.NotifyContentReady(_resource);

        var result = await _bridge.EvalAsync(_resource, "1 + 1");

        result.IsSuccess.Should().BeTrue();
        result.Value.Frame.Should().Be("top");
        result.Value.Value.Should().Be("\"echo:1 + 1\"");
    }

    [Test]
    public async Task EvalAsync_BeforeContentReady_BlocksUntilSignal()
    {
        _bridge.Register(
            _resource,
            evalAsync: _ => Task.FromResult("\"ok\""),
            reloadAsync: _ => Task.CompletedTask);

        var task = _bridge.EvalAsync(_resource, "x");

        // The task must not be observable as completed until content-ready fires.
        task.IsCompleted.Should().BeFalse();

        _bridge.NotifyContentReady(_resource);
        var result = await task;

        result.IsSuccess.Should().BeTrue();
    }

    [Test]
    public async Task EvalAsync_NeverReady_FailsWithTimeoutMessage()
    {
        // Use a short content-ready timeout so the test does not wait through the
        // production default (5 seconds) on every run.
        var fastBridge = new DocumentWebViewToolBridge(_commandService, _logger, _fileSystem, TimeSpan.FromMilliseconds(100));
        fastBridge.Register(
            _resource,
            evalAsync: _ => Task.FromResult("\"ok\""),
            reloadAsync: _ => Task.CompletedTask);

        var result = await fastBridge.EvalAsync(_resource, "x");

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("content-ready");

        // A page loaded in a hidden tab can miss its readiness signal, and showing the tab recovers it.
        result.FirstErrorMessage.Should().Contain("document_activate");
    }

    [Test]
    public async Task ReloadAsync_AfterRegistration_PassesClearCacheFlagAndDoesNotWaitForReady()
    {
        bool? observedClearCache = null;
        _bridge.Register(
            _resource,
            evalAsync: _ => Task.FromResult("null"),
            reloadAsync: clearCache =>
            {
                observedClearCache = clearCache;
                return Task.CompletedTask;
            });

        // Reload deliberately does not block on content-ready. Doing so would deadlock
        // the very signal an editor reload is supposed to refresh.
        var result = await _bridge.ReloadAsync(_resource, clearCache: true);

        result.IsSuccess.Should().BeTrue();
        observedClearCache.Should().BeTrue();
    }

    [Test]
    public async Task ReloadAsync_DelegateThrows_FailsAndUnsticksGateWithFailureReason()
    {
        // Use a short content-ready timeout so the test does not hang on the
        // 5-second production default if the gate is not properly opened.
        var fastBridge = new DocumentWebViewToolBridge(_commandService, _logger, _fileSystem, TimeSpan.FromMilliseconds(100));
        fastBridge.Register(
            _resource,
            evalAsync: _ => Task.FromResult("\"ok\""),
            reloadAsync: _ => throw new InvalidOperationException("boom"));
        fastBridge.NotifyContentReady(_resource);

        var reloadResult = await fastBridge.ReloadAsync(_resource, clearCache: false);
        reloadResult.IsFailure.Should().BeTrue();

        // Subsequent tool calls return the failure reason immediately rather
        // than waiting through the content-ready timeout.
        var evalResult = await fastBridge.EvalAsync(_resource, "x");
        evalResult.IsFailure.Should().BeTrue();
        evalResult.FirstErrorMessage.Should().Contain("last WebView reload failed");
        evalResult.FirstErrorMessage.Should().Contain("boom");
    }

    [Test]
    public async Task NotifyContentLoading_AfterFailure_ClearsTheFailureReason()
    {
        var fastBridge = new DocumentWebViewToolBridge(_commandService, _logger, _fileSystem, TimeSpan.FromMilliseconds(100));
        fastBridge.Register(
            _resource,
            evalAsync: _ => Task.FromResult("\"ok\""),
            reloadAsync: _ => throw new InvalidOperationException("previous failure"));
        fastBridge.NotifyContentReady(_resource);

        var reloadResult = await fastBridge.ReloadAsync(_resource, clearCache: false);
        reloadResult.IsFailure.Should().BeTrue();

        // The next navigation cycle starts loading and clears the sticky reason.
        fastBridge.NotifyContentLoading(_resource);
        fastBridge.NotifyContentReady(_resource);

        var evalResult = await fastBridge.EvalAsync(_resource, "x");
        evalResult.IsSuccess.Should().BeTrue();
    }

    [Test]
    public async Task ReloadAsync_ResetsContentReadyGate()
    {
        _bridge.Register(
            _resource,
            evalAsync: _ => Task.FromResult("\"ok\""),
            reloadAsync: _ => Task.CompletedTask);
        _bridge.NotifyContentReady(_resource);

        // First eval succeeds quickly because content is ready.
        var firstEval = await _bridge.EvalAsync(_resource, "x");
        firstEval.IsSuccess.Should().BeTrue();

        // Reload resets the gate, so the next eval should block until ready fires again.
        await _bridge.ReloadAsync(_resource, clearCache: false);

        var secondEvalTask = _bridge.EvalAsync(_resource, "x");
        secondEvalTask.IsCompleted.Should().BeFalse();
        _bridge.NotifyContentReady(_resource);
        var secondEval = await secondEvalTask;
        secondEval.IsSuccess.Should().BeTrue();
    }

    [Test]
    public async Task Register_SecondCall_ReplacesFirstEntry()
    {
        _bridge.Register(
            _resource,
            evalAsync: _ => Task.FromResult("\"first\""),
            reloadAsync: _ => Task.CompletedTask);

        _bridge.Register(
            _resource,
            evalAsync: _ => Task.FromResult("\"second\""),
            reloadAsync: _ => Task.CompletedTask);
        _bridge.NotifyContentReady(_resource);

        var result = await _bridge.EvalAsync(_resource, "x");

        result.Value.Value.Should().Be("\"second\"");
    }

    [Test]
    public async Task Rekey_MovesRegistrationToTheNewResource()
    {
        // A rename reuses the document view, so the registration has to move with it.
        ResourceKey.TryCreate("docs/renamed.md", out var renamedResource).Should().BeTrue();

        _bridge.Register(
            _resource,
            evalAsync: _ => Task.FromResult("\"alive\""),
            reloadAsync: _ => Task.CompletedTask);
        _bridge.NotifyContentReady(_resource);

        _bridge.Rekey(_resource, renamedResource);

        // The new key inherits the registration, including the open content-ready gate.
        var renamedResult = await _bridge.EvalAsync(renamedResource, "x");
        renamedResult.IsSuccess.Should().BeTrue();
        renamedResult.Value.Value.Should().Be("\"alive\"");

        // The old key no longer resolves, so the entry does not leak.
        var oldResult = await _bridge.EvalAsync(_resource, "x");
        oldResult.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task Rekey_PreservesConsoleHistory()
    {
        // The WebView itself is untouched by a rename, so entries captured under the old
        // key must remain readable under the new one.
        ResourceKey.TryCreate("docs/renamed.md", out var renamedResource).Should().BeTrue();

        var drained = false;
        _bridge.Register(
            _resource,
            evalAsync: PageWithoutFrames(_ =>
            {
                if (drained)
                {
                    return Task.FromResult(BuildFlushEnvelope("[]"));
                }
                drained = true;
                return Task.FromResult(BuildFlushEnvelope("[{\"level\":\"log\",\"timestampMs\":10,\"args\":[\"before-rename\"]}]"));
            }),
            reloadAsync: _ => Task.CompletedTask);
        _bridge.NotifyContentReady(_resource);

        await _bridge.GetConsoleAsync(_resource, new ConsoleQueryOptions());

        _bridge.Rekey(_resource, renamedResource);

        var result = await _bridge.GetConsoleAsync(renamedResource, new ConsoleQueryOptions());

        result.IsSuccess.Should().BeTrue();
        using var snapshot = JsonDocument.Parse(result.Value);
        var entries = snapshot.RootElement.GetProperty("entries");
        entries.GetArrayLength().Should().Be(1);
        entries[0].GetProperty("args")[0].GetString().Should().Be("before-rename");
    }

    [Test]
    public async Task Rekey_UnregisteredResource_DoesNotCreateAnEntry()
    {
        ResourceKey.TryCreate("docs/renamed.md", out var renamedResource).Should().BeTrue();

        _bridge.Rekey(_resource, renamedResource);

        var result = await _bridge.EvalAsync(renamedResource, "x");
        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task Unregister_RemovesEntry()
    {
        _bridge.Register(
            _resource,
            evalAsync: _ => Task.FromResult("\"alive\""),
            reloadAsync: _ => Task.CompletedTask);
        _bridge.NotifyContentReady(_resource);

        _bridge.Unregister(_resource);

        var result = await _bridge.EvalAsync(_resource, "x");
        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task EvalAsync_DelegateThrows_WrapsAsFailure()
    {
        _bridge.Register(
            _resource,
            evalAsync: _ => throw new InvalidOperationException("boom"),
            reloadAsync: _ => Task.CompletedTask);
        _bridge.NotifyContentReady(_resource);

        var result = await _bridge.EvalAsync(_resource, "x");

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("boom");
    }

    [Test]
    public async Task ReloadAsync_DelegateThrows_WrapsAsFailure()
    {
        _bridge.Register(
            _resource,
            evalAsync: _ => Task.FromResult("null"),
            reloadAsync: _ => throw new InvalidOperationException("kaboom"));

        var result = await _bridge.ReloadAsync(_resource, clearCache: false);

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("kaboom");
    }

    [Test]
    public async Task GetConsoleAsync_NoRegistration_Fails()
    {
        var result = await _bridge.GetConsoleAsync(_resource, new ConsoleQueryOptions());

        result.IsFailure.Should().BeTrue();
    }

    [Test]
    public async Task GetConsoleAsync_DrainAccumulatesBufferAcrossCalls()
    {
        // Simulates the shim returning two batches of console entries on successive
        // drains. The host must accumulate them so older entries remain visible.
        var calls = 0;
        _bridge.Register(
            _resource,
            evalAsync: PageWithoutFrames(_ =>
            {
                calls++;
                if (calls == 1)
                {
                    return Task.FromResult(BuildFlushEnvelope("[{\"level\":\"log\",\"timestampMs\":10,\"args\":[\"first\"]}]"));
                }
                return Task.FromResult(BuildFlushEnvelope("[{\"level\":\"warn\",\"timestampMs\":20,\"args\":[\"second\"]}]"));
            }),
            reloadAsync: _ => Task.CompletedTask);
        _bridge.NotifyContentReady(_resource);

        var firstResult = await _bridge.GetConsoleAsync(_resource, new ConsoleQueryOptions());
        var secondResult = await _bridge.GetConsoleAsync(_resource, new ConsoleQueryOptions());

        firstResult.IsSuccess.Should().BeTrue();
        secondResult.IsSuccess.Should().BeTrue();

        using var snapshot = JsonDocument.Parse(secondResult.Value);
        var entries = snapshot.RootElement.GetProperty("entries");
        entries.GetArrayLength().Should().Be(2);
        entries[0].GetProperty("args")[0].GetString().Should().Be("first");
        entries[1].GetProperty("args")[0].GetString().Should().Be("second");
    }

    [Test]
    public async Task GetConsoleAsync_FilterDebugByDefault()
    {
        _bridge.Register(
            _resource,
            evalAsync: PageWithoutFrames(_ => Task.FromResult(BuildFlushEnvelope(
                "[{\"level\":\"log\",\"timestampMs\":1,\"args\":[\"keep\"]}," +
                "{\"level\":\"debug\",\"timestampMs\":2,\"args\":[\"hide\"]}]"))),
            reloadAsync: _ => Task.CompletedTask);
        _bridge.NotifyContentReady(_resource);

        var result = await _bridge.GetConsoleAsync(_resource, new ConsoleQueryOptions(IncludeDebug: false));

        result.IsSuccess.Should().BeTrue();
        using var snapshot = JsonDocument.Parse(result.Value);
        snapshot.RootElement.GetProperty("entries").GetArrayLength().Should().Be(1);
        snapshot.RootElement.GetProperty("entries")[0].GetProperty("args")[0].GetString().Should().Be("keep");
    }

    [Test]
    public async Task GetConsoleAsync_SinceTimestampFiltersOlderEntries()
    {
        _bridge.Register(
            _resource,
            evalAsync: PageWithoutFrames(_ => Task.FromResult(BuildFlushEnvelope(
                "[{\"level\":\"log\",\"timestampMs\":10,\"args\":[\"old\"]}," +
                "{\"level\":\"log\",\"timestampMs\":20,\"args\":[\"new\"]}]"))),
            reloadAsync: _ => Task.CompletedTask);
        _bridge.NotifyContentReady(_resource);

        var result = await _bridge.GetConsoleAsync(_resource, new ConsoleQueryOptions(SinceTimestampMs: 15));

        result.IsSuccess.Should().BeTrue();
        using var snapshot = JsonDocument.Parse(result.Value);
        snapshot.RootElement.GetProperty("entries").GetArrayLength().Should().Be(1);
        snapshot.RootElement.GetProperty("entries")[0].GetProperty("args")[0].GetString().Should().Be("new");
    }

    [Test]
    public async Task GetConsoleAsync_BufferSurvivesReload()
    {
        // Drain returns first batch, reload happens, then drain returns second batch.
        var calls = 0;
        _bridge.Register(
            _resource,
            evalAsync: PageWithoutFrames(_ =>
            {
                calls++;
                if (calls == 1)
                {
                    return Task.FromResult(BuildFlushEnvelope("[{\"level\":\"error\",\"timestampMs\":1,\"args\":[\"pre-reload\"]}]"));
                }
                return Task.FromResult(BuildFlushEnvelope("[{\"level\":\"log\",\"timestampMs\":2,\"args\":[\"post-reload\"]}]"));
            }),
            reloadAsync: _ => Task.CompletedTask);
        _bridge.NotifyContentReady(_resource);

        await _bridge.ReloadAsync(_resource, clearCache: false);
        _bridge.NotifyContentReady(_resource);

        var result = await _bridge.GetConsoleAsync(_resource, new ConsoleQueryOptions());

        result.IsSuccess.Should().BeTrue();
        using var snapshot = JsonDocument.Parse(result.Value);
        var entries = snapshot.RootElement.GetProperty("entries");
        entries.GetArrayLength().Should().Be(2);
        entries[0].GetProperty("args")[0].GetString().Should().Be("pre-reload");
        entries[1].GetProperty("args")[0].GetString().Should().Be("post-reload");
    }

    [Test]
    public async Task GetNetworkAsync_BufferSurvivesReload()
    {
        // Dispatch by expression so the flushNetwork counter is not
        // perturbed by ReloadAsync's flushConsole drain.
        var networkCalls = 0;
        _bridge.Register(
            _resource,
            evalAsync: PageWithoutFrames(expression =>
            {
                if (expression.Contains("flushNetwork"))
                {
                    networkCalls++;
                    if (networkCalls == 1)
                    {
                        return Task.FromResult(BuildFlushEnvelope(
                            "[{\"id\":1,\"type\":\"fetch\",\"method\":\"GET\",\"url\":\"https://example.com/pre-reload\",\"status\":200,\"startTimeMs\":1}]"));
                    }
                    return Task.FromResult(BuildFlushEnvelope(
                        "[{\"id\":2,\"type\":\"fetch\",\"method\":\"GET\",\"url\":\"https://example.com/post-reload\",\"status\":200,\"startTimeMs\":2}]"));
                }
                return Task.FromResult(BuildFlushEnvelope("[]"));
            }),
            reloadAsync: _ => Task.CompletedTask);
        _bridge.NotifyContentReady(_resource);

        await _bridge.ReloadAsync(_resource, clearCache: false);
        _bridge.NotifyContentReady(_resource);

        var result = await _bridge.GetNetworkAsync(_resource, new NetworkQueryOptions());

        result.IsSuccess.Should().BeTrue();
        using var snapshot = JsonDocument.Parse(result.Value);
        var entries = snapshot.RootElement.GetProperty("entries");
        entries.GetArrayLength().Should().Be(2);
        entries[0].GetProperty("url").GetString().Should().Be("https://example.com/pre-reload");
        entries[1].GetProperty("url").GetString().Should().Be("https://example.com/post-reload");
    }

    [Test]
    public async Task GetHtmlAsync_PropagatesShimError()
    {
        _bridge.Register(
            _resource,
            evalAsync: _ => Task.FromResult("{\"ok\":false,\"error\":\"no element matches selector '#missing'\"}"),
            reloadAsync: _ => Task.CompletedTask);
        _bridge.NotifyContentReady(_resource);

        var result = await _bridge.GetHtmlAsync(_resource, new GetHtmlOptions(Selector: "#missing"));

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("no element matches");
    }

    [Test]
    public async Task GetHtmlAsync_ShimMissing_FailsWithDescriptiveError()
    {
        _bridge.Register(
            _resource,
            evalAsync: _ => Task.FromResult("null"),
            reloadAsync: _ => Task.CompletedTask);
        _bridge.NotifyContentReady(_resource);

        var result = await _bridge.GetHtmlAsync(_resource, new GetHtmlOptions());

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("shim");
    }

    [Test]
    public async Task GetHtmlAsync_ReturnsRawValueJson()
    {
        _bridge.Register(
            _resource,
            evalAsync: _ => Task.FromResult("{\"ok\":true,\"value\":{\"selector\":null,\"html\":\"<body></body>\"}}"),
            reloadAsync: _ => Task.CompletedTask);
        _bridge.NotifyContentReady(_resource);

        var result = await _bridge.GetHtmlAsync(_resource, new GetHtmlOptions());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("<body></body>");
    }

    [Test]
    public async Task QueryAsync_ForwardsArgumentsAndReturnsValue()
    {
        string? capturedExpression = null;
        _bridge.Register(
            _resource,
            evalAsync: expression =>
            {
                capturedExpression = expression;
                return Task.FromResult("{\"ok\":true,\"value\":{\"mode\":\"role\",\"totalMatches\":0,\"returned\":0,\"elements\":[]}}");
            },
            reloadAsync: _ => Task.CompletedTask);
        _bridge.NotifyContentReady(_resource);

        var result = await _bridge.QueryAsync(_resource, new QueryOptions(new RoleQuery("button", "Save")));

        result.IsSuccess.Should().BeTrue();
        capturedExpression.Should().NotBeNull();
        capturedExpression!.Should().Contain("\"query\"");
        // Args are forwarded as a JSON-encoded string literal so the bridge can parse them.
        capturedExpression.Should().Contain("button");
        capturedExpression.Should().Contain("Save");
    }
}

public partial class DocumentWebViewToolBridgeTests
{
    private static string BuildFlushEnvelope(string entriesJsonArray)
    {
        return "{\"ok\":true,\"value\":" + entriesJsonArray + "}";
    }

    private static string BuildShimValue(string valueJson)
    {
        return "{\"ok\":true,\"value\":" + valueJson + "}";
    }

    private static bool CallsHandler(string expression, string handlerName)
    {
        return expression.Contains($"\"{handlerName}\"");
    }

    // Answers the shim's frame requests the way a page with no frames does, and hands every other expression to
    // the evaluator.
    private static Func<string, Task<string>> PageWithoutFrames(Func<string, Task<string>> evaluate)
    {
        return expression =>
        {
            if (CallsHandler(expression, "resolveFrame") ||
                CallsHandler(expression, "reload"))
            {
                return Task.FromResult(BuildShimValue("{\"frame\":\"top\",\"top\":true}"));
            }

            return evaluate(expression);
        };
    }

    // Answers the shim's frame requests the way a page whose content frame is #preview does, and hands every
    // other expression to the evaluator.
    private static Func<string, Task<string>> PageWithContentFrame(Func<string, Task<string>> evaluate)
    {
        return expression =>
        {
            if (CallsHandler(expression, "resolveFrame") ||
                CallsHandler(expression, "reload"))
            {
                return Task.FromResult(BuildShimValue("{\"frame\":\"#preview\",\"top\":false}"));
            }

            return evaluate(expression);
        };
    }

    [Test]
    public async Task GetHtmlAsync_WhileTheFrameLoads_CallsAgainUntilItHasLoaded()
    {
        var calls = 0;
        _bridge.Register(
            _resource,
            evalAsync: _ =>
            {
                calls++;
                if (calls < 3)
                {
                    return Task.FromResult("{\"ok\":false,\"pending\":true,\"frame\":\"#preview\",\"error\":\"loading\"}");
                }
                return Task.FromResult(BuildShimValue("{\"frame\":\"#preview\",\"selector\":null,\"html\":\"<html></html>\"}"));
            },
            reloadAsync: _ => Task.CompletedTask);
        _bridge.NotifyContentReady(_resource);

        var result = await _bridge.GetHtmlAsync(_resource, new GetHtmlOptions());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("#preview");
        calls.Should().Be(3);
    }

    [Test]
    public async Task GetHtmlAsync_FrameThatNeverLoads_FailsNamingTheFrame()
    {
        var fastBridge = new DocumentWebViewToolBridge(_commandService, _logger, _fileSystem, TimeSpan.FromMilliseconds(100));
        fastBridge.Register(
            _resource,
            evalAsync: _ => Task.FromResult("{\"ok\":false,\"pending\":true,\"frame\":\"#preview\",\"error\":\"loading\"}"),
            reloadAsync: _ => Task.CompletedTask);
        fastBridge.NotifyContentReady(_resource);

        var result = await fastBridge.GetHtmlAsync(_resource, new GetHtmlOptions());

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("'#preview' to finish loading");
        result.FirstErrorMessage.Should().Contain("document_activate");
    }

    [Test]
    public async Task GetHtmlAsync_PassesTheFrameToTheShim()
    {
        string? capturedExpression = null;
        _bridge.Register(
            _resource,
            evalAsync: expression =>
            {
                capturedExpression = expression;
                return Task.FromResult(BuildShimValue("{\"frame\":\"top\",\"selector\":null,\"html\":\"<html></html>\"}"));
            },
            reloadAsync: _ => Task.CompletedTask);
        _bridge.NotifyContentReady(_resource);

        await _bridge.GetHtmlAsync(_resource, new GetHtmlOptions(Frame: "top"));

        ReadInvokeArgs(capturedExpression!).GetProperty("frame").GetString().Should().Be("top");
    }

    // The args travel to the shim as a JSON string literal, the last argument of the invoke call.
    private static JsonElement ReadInvokeArgs(string expression)
    {
        var invokeStart = expression.LastIndexOf("b.invoke(", StringComparison.Ordinal);
        var argsStart = expression.IndexOf(',', invokeStart) + 1;
        var argsEnd = expression.LastIndexOf(");", StringComparison.Ordinal);
        var argsJson = JsonSerializer.Deserialize<string>(expression[argsStart..argsEnd])!;

        using var document = JsonDocument.Parse(argsJson);
        return document.RootElement.Clone();
    }

    [Test]
    public async Task EvalAsync_InAFrame_EvaluatesThroughTheShimAndNamesTheFrame()
    {
        var evaluatedDirectly = false;
        _bridge.Register(
            _resource,
            evalAsync: PageWithContentFrame(expression =>
            {
                if (CallsHandler(expression, "evaluate"))
                {
                    return Task.FromResult(BuildShimValue("{\"frame\":\"#preview\",\"valueJson\":\"\\\"Framed\\\"\"}"));
                }

                evaluatedDirectly = true;
                return Task.FromResult("null");
            }),
            reloadAsync: _ => Task.CompletedTask);
        _bridge.NotifyContentReady(_resource);

        var result = await _bridge.EvalAsync(_resource, "document.title");

        result.IsSuccess.Should().BeTrue();
        result.Value.Frame.Should().Be("#preview");
        result.Value.Value.Should().Be("\"Framed\"");
        evaluatedDirectly.Should().BeFalse();
    }

    [Test]
    public async Task EvalAsync_OnThePageItself_EvaluatesTheExpressionDirectly()
    {
        string? directExpression = null;
        _bridge.Register(
            _resource,
            evalAsync: PageWithoutFrames(expression =>
            {
                directExpression = expression;
                return Task.FromResult("2");
            }),
            reloadAsync: _ => Task.CompletedTask);
        _bridge.NotifyContentReady(_resource);

        var result = await _bridge.EvalAsync(_resource, "1 + 1");

        result.Value.Should().Be(new WebViewEvalResult("top", "2"));
        directExpression.Should().Be("1 + 1");
    }

    [Test]
    public async Task EvalAsync_PageWithoutTheShim_EvaluatesThePageButCannotReachAFrame()
    {
        _bridge.Register(
            _resource,
            evalAsync: expression =>
            {
                if (CallsHandler(expression, "resolveFrame"))
                {
                    return Task.FromResult("{\"ok\":false,\"missingShim\":true,\"error\":\"WebView tool bridge shim not present\"}");
                }
                return Task.FromResult("2");
            },
            reloadAsync: _ => Task.CompletedTask);
        _bridge.NotifyContentReady(_resource);

        var pageResult = await _bridge.EvalAsync(_resource, "1 + 1");
        var frameResult = await _bridge.EvalAsync(_resource, "1 + 1", "#preview");

        pageResult.Value.Should().Be(new WebViewEvalResult("top", "2"));
        frameResult.IsFailure.Should().BeTrue();
        frameResult.FirstErrorMessage.Should().Contain("'#preview' cannot be reached");
    }

    [Test]
    public async Task ReloadAsync_Frame_ReloadsThroughTheShimAndKeepsTheGateOpen()
    {
        var reloadedTheWebView = false;
        _bridge.Register(
            _resource,
            evalAsync: PageWithContentFrame(_ => Task.FromResult(BuildFlushEnvelope("[]"))),
            reloadAsync: _ =>
            {
                reloadedTheWebView = true;
                return Task.CompletedTask;
            });
        _bridge.NotifyContentReady(_resource);

        var result = await _bridge.ReloadAsync(_resource, clearCache: true);

        result.Value.Should().Be("#preview");
        reloadedTheWebView.Should().BeFalse();

        // Only the frame reloaded, so the page's content-ready gate stays open and the next call goes ahead at once.
        var htmlTask = _bridge.GetHtmlAsync(_resource, new GetHtmlOptions());
        htmlTask.IsCompleted.Should().BeTrue();
        (await htmlTask).IsSuccess.Should().BeTrue();
    }

    [Test]
    public async Task ReloadAsync_PageItself_ReloadsTheWebView()
    {
        var reloadedTheWebView = false;
        _bridge.Register(
            _resource,
            evalAsync: PageWithoutFrames(_ => Task.FromResult(BuildFlushEnvelope("[]"))),
            reloadAsync: _ =>
            {
                reloadedTheWebView = true;
                return Task.CompletedTask;
            });

        var result = await _bridge.ReloadAsync(_resource, clearCache: false);

        result.Value.Should().Be("top");
        reloadedTheWebView.Should().BeTrue();
    }

    [Test]
    public async Task GetConsoleAsync_ReturnsOnlyTheEntriesOfTheFrame()
    {
        _bridge.Register(
            _resource,
            evalAsync: PageWithContentFrame(_ => Task.FromResult(BuildFlushEnvelope(
                "[{\"level\":\"log\",\"timestampMs\":1,\"args\":[\"shell\"],\"frame\":\"top\"}," +
                "{\"level\":\"log\",\"timestampMs\":2,\"args\":[\"page\"],\"frame\":\"#preview\"}]"))),
            reloadAsync: _ => Task.CompletedTask);
        _bridge.NotifyContentReady(_resource);

        var result = await _bridge.GetConsoleAsync(_resource, new ConsoleQueryOptions());

        result.IsSuccess.Should().BeTrue();
        using var snapshot = JsonDocument.Parse(result.Value);
        var root = snapshot.RootElement;
        root.GetProperty("frame").GetString().Should().Be("#preview");
        root.GetProperty("totalAccumulated").GetInt32().Should().Be(1);
        var entries = root.GetProperty("entries");
        entries.GetArrayLength().Should().Be(1);
        entries[0].GetProperty("args")[0].GetString().Should().Be("page");
        entries[0].TryGetProperty("frame", out _).Should().BeFalse();
    }

    [Test]
    public async Task ScreenshotAsync_OfAFrame_CapturesTheFrameAndNamesIt()
    {
        ScreenshotRequest? capturedRequest = null;
        _bridge.Register(
            _resource,
            evalAsync: expression => Task.FromResult(BuildShimValue(
                "{\"frame\":\"#preview\",\"x\":100,\"y\":40,\"width\":600,\"height\":400}")),
            reloadAsync: _ => Task.CompletedTask,
            screenshotAsync: request =>
            {
                capturedRequest = request;
                return Task.FromResult(new ScreenshotData("jpeg", 600, 400, [1, 2, 3]));
            });
        _bridge.NotifyContentReady(_resource);

        var result = await _bridge.ScreenshotAsync(_resource, new ScreenshotOptions(MaxEdge: 0));

        result.IsSuccess.Should().BeTrue();
        result.Value.Frame.Should().Be("#preview");
        capturedRequest!.Clip.Should().Be(new ScreenshotClip(100, 40, 600, 400, 1.0));
    }
}
