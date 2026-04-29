using System.Text.Json;
using Celbridge.Resources;
using Celbridge.WebHost;
using Celbridge.WebHost.Services;

namespace Celbridge.Tests.WebView;

[TestFixture]
public partial class DocumentWebViewToolBridgeTests
{
    private DocumentWebViewToolBridge _bridge = null!;
    private ResourceKey _resource;

    [SetUp]
    public void SetUp()
    {
        _bridge = new DocumentWebViewToolBridge();
        ResourceKey.TryCreate("docs/readme.md", out _resource).Should().BeTrue();
    }

    [Test]
    public async Task EvalAsync_NoRegistration_ReturnsFailureWithMessage()
    {
        var result = await _bridge.EvalAsync(_resource, "1 + 1");

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain(_resource.ToString());
        result.FirstErrorMessage.Should().Contain("webview_*");
    }

    [Test]
    public async Task ReloadAsync_NoRegistration_ReturnsFailureWithMessage()
    {
        var result = await _bridge.ReloadAsync(_resource, clearCache: false);

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain(_resource.ToString());
    }

    [Test]
    public async Task EvalAsync_AfterRegistrationAndContentReady_ReturnsDelegateResult()
    {
        _bridge.Register(
            _resource,
            evalAsync: expression => Task.FromResult($"\"echo:{expression}\""),
            reloadAsync: _ => Task.CompletedTask);
        _bridge.NotifyContentReady(_resource);

        var result = await _bridge.EvalAsync(_resource, "1 + 1");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("\"echo:1 + 1\"");
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
        var fastBridge = new DocumentWebViewToolBridge(TimeSpan.FromMilliseconds(100));
        fastBridge.Register(
            _resource,
            evalAsync: _ => Task.FromResult("\"ok\""),
            reloadAsync: _ => Task.CompletedTask);

        var result = await fastBridge.EvalAsync(_resource, "x");

        result.IsFailure.Should().BeTrue();
        result.FirstErrorMessage.Should().Contain("content-ready");
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

        // Reload deliberately does not block on content-ready; it would deadlock the
        // very signal an editor reload is supposed to refresh.
        var result = await _bridge.ReloadAsync(_resource, clearCache: true);

        result.IsSuccess.Should().BeTrue();
        observedClearCache.Should().BeTrue();
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

        // Reload resets the gate; the next eval should block until ready fires again.
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

        result.Value.Should().Be("\"second\"");
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
            evalAsync: _ =>
            {
                calls++;
                if (calls == 1)
                {
                    return Task.FromResult(BuildFlushEnvelope("[{\"level\":\"log\",\"timestampMs\":10,\"args\":[\"first\"]}]"));
                }
                return Task.FromResult(BuildFlushEnvelope("[{\"level\":\"warn\",\"timestampMs\":20,\"args\":[\"second\"]}]"));
            },
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
            evalAsync: _ => Task.FromResult(BuildFlushEnvelope(
                "[{\"level\":\"log\",\"timestampMs\":1,\"args\":[\"keep\"]}," +
                "{\"level\":\"debug\",\"timestampMs\":2,\"args\":[\"hide\"]}]")),
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
            evalAsync: _ => Task.FromResult(BuildFlushEnvelope(
                "[{\"level\":\"log\",\"timestampMs\":10,\"args\":[\"old\"]}," +
                "{\"level\":\"log\",\"timestampMs\":20,\"args\":[\"new\"]}]")),
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
            evalAsync: _ =>
            {
                calls++;
                if (calls == 1)
                {
                    return Task.FromResult(BuildFlushEnvelope("[{\"level\":\"error\",\"timestampMs\":1,\"args\":[\"pre-reload\"]}]"));
                }
                return Task.FromResult(BuildFlushEnvelope("[{\"level\":\"log\",\"timestampMs\":2,\"args\":[\"post-reload\"]}]"));
            },
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
}
