using System.Collections.Concurrent;
using System.Text.Json;
using Celbridge.Commands;
using Celbridge.Logging;

namespace Celbridge.WebHost.Services;

/// <summary>
/// Maintains a registry of WebViews registered with the webview_* MCP tool namespace,
/// keyed by their document resource. Routes eval and reload calls from those tools
/// to the registered WebView via the delegates supplied at registration time. The
/// delegates are responsible for marshalling onto the UI thread.
/// </summary>
public partial class DocumentWebViewToolBridge : IDocumentWebViewToolBridge
{
    private const string ShimRelativePath = "Celbridge.WebHost/Web/celbridge-client/core/webview-tools-shim.js";

    // Default upper bound on how long a tool call waits for the editor's content-ready
    // signal before failing. Generous enough for heavyweight editors (markdown preview,
    // Monaco) that import packages on first paint.
    private static readonly TimeSpan DefaultContentReadyTimeout = TimeSpan.FromSeconds(5);

    private readonly TimeSpan _contentReadyTimeout;
    private readonly ICommandService _commandService;
    private readonly ILogger<DocumentWebViewToolBridge> _logger;

    public DocumentWebViewToolBridge(ICommandService commandService, ILogger<DocumentWebViewToolBridge> logger)
        : this(commandService, logger, DefaultContentReadyTimeout) { }

    // Test-friendly constructor so unit tests can use a short timeout without
    // waiting through the 5-second default for every gated-but-never-ready case.
    internal DocumentWebViewToolBridge(ICommandService commandService, ILogger<DocumentWebViewToolBridge> logger, TimeSpan contentReadyTimeout)
    {
        _contentReadyTimeout = contentReadyTimeout;
        _commandService = commandService;
        _logger = logger;
    }

    // Cap accumulated console history per resource. Older entries are evicted FIFO
    // when the cap is hit. The shim has its own bounded ring. This cap protects the
    // host from a runaway editor that logs forever.
    private const int ConsoleHistoryCap = 2000;

    // Same cap, applied to fetch/XHR activity so a chatty page cannot grow the
    // host accumulator without bound between drains.
    private const int NetworkHistoryCap = 2000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ConcurrentDictionary<ResourceKey, WebViewToolBridgeEntry> _entries = new();
    private readonly object _shimLock = new();
    private string? _cachedShimScript;

    public string GetShimScript()
    {
        if (_cachedShimScript is not null)
        {
            return _cachedShimScript;
        }

        lock (_shimLock)
        {
            if (_cachedShimScript is not null)
            {
                return _cachedShimScript;
            }

            var fullPath = System.IO.Path.Combine(AppContext.BaseDirectory, ShimRelativePath);
            try
            {
                _cachedShimScript = File.ReadAllText(fullPath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to load the WebView tool bridge shim from '{fullPath}'. The file is expected to ship with the host build output.",
                    ex);
            }

            return _cachedShimScript;
        }
    }

    public void Register(
        ResourceKey resource,
        Func<string, Task<string>> evalAsync,
        Func<bool, Task> reloadAsync,
        Func<ScreenshotRequest, Task<ScreenshotData>>? screenshotAsync = null)
    {
        var entry = new WebViewToolBridgeEntry(evalAsync, reloadAsync, screenshotAsync);
        _entries[resource] = entry;
    }

    public void Unregister(ResourceKey resource)
    {
        _entries.TryRemove(resource, out _);
    }

    public void NotifyContentReady(ResourceKey resource)
    {
        if (_entries.TryGetValue(resource, out var entry))
        {
            entry.NotifyContentReady();
        }
    }

    public void NotifyContentLoading(ResourceKey resource)
    {
        if (_entries.TryGetValue(resource, out var entry))
        {
            entry.NotifyContentLoading();
        }
    }

    public void NotifyContentFailed(ResourceKey resource, string reason)
    {
        if (_entries.TryGetValue(resource, out var entry))
        {
            entry.NotifyContentFailed(reason);
        }
    }

    public async Task<Result<string>> EvalAsync(ResourceKey resource, string expression)
    {
        if (!_entries.TryGetValue(resource, out var entry))
        {
            return Result.Fail(await GetUnavailableReasonAsync(resource));
        }

        var waitResult = await entry.WaitForContentReadyAsync(_contentReadyTimeout);
        if (waitResult.IsFailure)
        {
            return Result.Fail(waitResult.FirstErrorMessage);
        }

        try
        {
            var result = await entry.EvalAsync(expression);
            return result;
        }
        catch (Exception ex)
        {
            return Result.Fail($"WebView eval failed for resource '{resource}': {ex.Message}")
                .WithException(ex);
        }
    }

    public async Task<Result> ReloadAsync(ResourceKey resource, bool clearCache)
    {
        if (!_entries.TryGetValue(resource, out var entry))
        {
            return Result.Fail(await GetUnavailableReasonAsync(resource));
        }

        // Flush in-page buffers into the host accumulator so entries captured
        // before the reload survive it.
        await TryDrainConsoleAsync(entry);
        await TryDrainNetworkAsync(entry);

        entry.NotifyContentLoading();
        try
        {
            await entry.ReloadAsync(clearCache);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            // The reload delegate failed before NavigationCompleted could fire,
            // so the gate would otherwise stay closed and every later tool call
            // would block until the 5-second timeout. Mark the entry as failed
            // so subsequent tool calls fail fast with the reload error rather
            // than dispatching against a broken page state.
            var reason = $"The last WebView reload failed: {ex.Message}";
            entry.NotifyContentFailed(reason);
            return Result.Fail($"WebView reload failed for resource '{resource}': {ex.Message}")
                .WithException(ex);
        }
    }

    public async Task<Result<string>> GetConsoleAsync(ResourceKey resource, ConsoleQueryOptions options)
    {
        if (!_entries.TryGetValue(resource, out var entry))
        {
            return Result.Fail(await GetUnavailableReasonAsync(resource));
        }

        var waitResult = await entry.WaitForContentReadyAsync(_contentReadyTimeout);
        if (waitResult.IsFailure)
        {
            return Result.Fail(waitResult.FirstErrorMessage);
        }

        await TryDrainConsoleAsync(entry);

        var snapshot = entry.SnapshotConsole(options);
        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    public async Task<Result<string>> GetHtmlAsync(ResourceKey resource, GetHtmlOptions options)
    {
        var args = new
        {
            selector = options.Selector,
            maxDepth = options.MaxDepth
        };

        return await InvokeShimHandlerAsync(resource, "getHtml", args);
    }

    public async Task<Result<string>> QueryAsync(ResourceKey resource, QueryOptions options)
    {
        string? role = null;
        string? name = null;
        string? text = null;
        string? selector = null;

        switch (options.Mode)
        {
            case RoleQuery roleQuery:
                role = roleQuery.Role;
                name = roleQuery.Name;
                break;
            case TextQuery textQuery:
                text = textQuery.Text;
                break;
            case SelectorQuery selectorQuery:
                selector = selectorQuery.Selector;
                break;
        }

        var args = new
        {
            role,
            name,
            text,
            selector,
            maxResults = options.MaxResults
        };

        return await InvokeShimHandlerAsync(resource, "query", args);
    }

    public async Task<Result<string>> InspectAsync(ResourceKey resource, InspectOptions options)
    {
        var args = new
        {
            selector = options.Selector,
            childPreviewLimit = options.ChildPreviewLimit
        };

        return await InvokeShimHandlerAsync(resource, "inspect", args);
    }

    public async Task<Result<string>> ClickAsync(ResourceKey resource, ClickOptions options)
    {
        var args = new
        {
            selector = options.Selector
        };

        return await InvokeShimHandlerAsync(resource, "click", args);
    }

    public async Task<Result<string>> FillAsync(ResourceKey resource, FillOptions options)
    {
        var args = new
        {
            selector = options.Selector,
            value = options.Value
        };

        return await InvokeShimHandlerAsync(resource, "fill", args);
    }

    public async Task<Result<string>> GetNetworkAsync(ResourceKey resource, NetworkQueryOptions options)
    {
        if (!_entries.TryGetValue(resource, out var entry))
        {
            return Result.Fail(await GetUnavailableReasonAsync(resource));
        }

        var waitResult = await entry.WaitForContentReadyAsync(_contentReadyTimeout);
        if (waitResult.IsFailure)
        {
            return Result.Fail(waitResult.FirstErrorMessage);
        }

        await TryDrainNetworkAsync(entry);

        var snapshot = entry.SnapshotNetwork(options);
        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    public async Task<Result<ScreenshotData>> ScreenshotAsync(ResourceKey resource, ScreenshotOptions options)
    {
        if (!_entries.TryGetValue(resource, out var entry))
        {
            return Result.Fail(await GetUnavailableReasonAsync(resource));
        }

        if (!entry.HasScreenshotDelegate)
        {
            return Result.Fail($"Screenshot is not supported for the WebView registered for resource '{resource}'. The hosting platform did not provide a native screenshot API.");
        }

        var format = string.IsNullOrEmpty(options.Format) ? "jpeg" : options.Format.ToLowerInvariant();
        if (format != "jpeg" && format != "png")
        {
            return Result.Fail($"Unsupported screenshot format '{options.Format}'. Use 'jpeg' or 'png'.");
        }

        var quality = Math.Clamp(options.Quality, 1, 100);

        var waitResult = await entry.WaitForContentReadyAsync(_contentReadyTimeout);
        if (waitResult.IsFailure)
        {
            return Result.Fail(waitResult.FirstErrorMessage);
        }

        Result<ScreenshotClip> clipResult = string.IsNullOrEmpty(options.Selector)
            ? await ResolveViewportClipAsync(entry, resource, options.MaxEdge)
            : await ResolveSelectorRectAsync(entry, resource, options.Selector!, options.MaxEdge);
        if (clipResult.IsFailure)
        {
            return Result.Fail(clipResult.FirstErrorMessage);
        }
        var clip = clipResult.Value;

        var settleMs = options.SettleMs < 0 ? 0 : options.SettleMs;
        var request = new ScreenshotRequest(format, quality, clip, settleMs);

        try
        {
            var data = await entry.ScreenshotAsync(request);
            return data;
        }
        catch (Exception ex)
        {
            return Result.Fail($"WebView screenshot failed for resource '{resource}': {ex.Message}")
                .WithException(ex);
        }
    }

    private static async Task<Result<ScreenshotClip>> ResolveSelectorRectAsync(
        WebViewToolBridgeEntry entry,
        ResourceKey resource,
        string selector,
        int maxEdge)
    {
        var argsJson = JsonSerializer.Serialize(new { selector }, JsonOptions);
        var expression = BuildInvokeExpression("getRect", argsJson);
        string evalResultJson;
        try
        {
            evalResultJson = await entry.EvalAsync(expression);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to resolve selector rectangle for screenshot on resource '{resource}': {ex.Message}")
                .WithException(ex);
        }

        var unwrap = UnwrapShimResult(evalResultJson, "getRect", resource);
        if (unwrap.IsFailure)
        {
            return Result.Fail(unwrap.FirstErrorMessage);
        }

        try
        {
            using var doc = JsonDocument.Parse(unwrap.Value);
            var root = doc.RootElement;
            var x = root.GetProperty("x").GetDouble();
            var y = root.GetProperty("y").GetDouble();
            var width = root.GetProperty("width").GetDouble();
            var height = root.GetProperty("height").GetDouble();
            if (width <= 0 || height <= 0)
            {
                return Result.Fail($"Element matched by selector '{selector}' has zero size; nothing to capture.");
            }
            var scale = ComputeScale(width, height, maxEdge);
            return new ScreenshotClip(x, y, width, height, scale);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to parse selector rectangle for screenshot on resource '{resource}': {ex.Message}")
                .WithException(ex);
        }
    }

    private static async Task<Result<ScreenshotClip>> ResolveViewportClipAsync(
        WebViewToolBridgeEntry entry,
        ResourceKey resource,
        int maxEdge)
    {
        var argsJson = "{}";
        var expression = BuildInvokeExpression("getViewport", argsJson);
        string evalResultJson;
        try
        {
            evalResultJson = await entry.EvalAsync(expression);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to resolve viewport size for screenshot on resource '{resource}': {ex.Message}")
                .WithException(ex);
        }

        var unwrap = UnwrapShimResult(evalResultJson, "getViewport", resource);
        if (unwrap.IsFailure)
        {
            return Result.Fail(unwrap.FirstErrorMessage);
        }

        try
        {
            using var doc = JsonDocument.Parse(unwrap.Value);
            var root = doc.RootElement;
            var width = root.GetProperty("width").GetDouble();
            var height = root.GetProperty("height").GetDouble();
            if (width <= 0 || height <= 0)
            {
                return Result.Fail($"WebView reported a non-positive viewport size ({width}x{height}); cannot compute a screenshot clip.");
            }
            var scale = ComputeScale(width, height, maxEdge);
            return new ScreenshotClip(0, 0, width, height, scale);
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to parse viewport size for screenshot on resource '{resource}': {ex.Message}")
                .WithException(ex);
        }
    }

    private static double ComputeScale(double width, double height, int maxEdge)
    {
        if (maxEdge <= 0)
        {
            return 1.0;
        }

        var longer = Math.Max(width, height);
        if (longer <= maxEdge)
        {
            return 1.0;
        }

        return maxEdge / longer;
    }

    private async Task TryDrainNetworkAsync(WebViewToolBridgeEntry entry)
    {
        try
        {
            var argsJson = "{}";
            var expression = BuildInvokeExpression("flushNetwork", argsJson);
            var raw = await entry.EvalAsync(expression);

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Null)
            {
                return;
            }

            if (!root.TryGetProperty("ok", out var okElement) || !okElement.GetBoolean())
            {
                return;
            }

            if (!root.TryGetProperty("value", out var valueElement) || valueElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var item in valueElement.EnumerateArray())
            {
                var networkEntry = NetworkEntry.FromJson(item);
                if (networkEntry is not null)
                {
                    entry.AppendNetworkEntry(networkEntry, NetworkHistoryCap);
                }
            }
        }
        catch (Exception ex)
        {
            // Best-effort: a missing shim or a failed eval should not surface as
            // a tool error. Log at Debug so unexpected failures are still traceable.
            _logger.LogDebug(ex, "Network drain failed");
        }
    }

    private async Task<Result<string>> InvokeShimHandlerAsync(ResourceKey resource, string handlerName, object args)
    {
        if (!_entries.TryGetValue(resource, out var entry))
        {
            return Result.Fail(await GetUnavailableReasonAsync(resource));
        }

        var waitResult = await entry.WaitForContentReadyAsync(_contentReadyTimeout);
        if (waitResult.IsFailure)
        {
            return Result.Fail(waitResult.FirstErrorMessage);
        }

        var argsJson = JsonSerializer.Serialize(args, JsonOptions);
        var expression = BuildInvokeExpression(handlerName, argsJson);

        string evalResultJson;
        try
        {
            evalResultJson = await entry.EvalAsync(expression);
        }
        catch (Exception ex)
        {
            return Result.Fail($"WebView shim invocation failed for handler '{handlerName}' on resource '{resource}': {ex.Message}")
                .WithException(ex);
        }

        return UnwrapShimResult(evalResultJson, handlerName, resource);
    }

    private async Task TryDrainConsoleAsync(WebViewToolBridgeEntry entry)
    {
        try
        {
            var argsJson = "{}";
            var expression = BuildInvokeExpression("flushConsole", argsJson);
            var raw = await entry.EvalAsync(expression);

            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Null)
            {
                return;
            }

            if (!root.TryGetProperty("ok", out var okElement) || !okElement.GetBoolean())
            {
                return;
            }

            if (!root.TryGetProperty("value", out var valueElement) || valueElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var item in valueElement.EnumerateArray())
            {
                var consoleEntry = ConsoleEntry.FromJson(item);
                if (consoleEntry is not null)
                {
                    entry.AppendConsoleEntry(consoleEntry, ConsoleHistoryCap);
                }
            }
        }
        catch (Exception ex)
        {
            // The shim may not be installed (cross-origin frame, or the page hasn't
            // navigated yet). Drain is best-effort: log at Debug rather than
            // surfacing the failure to the caller.
            _logger.LogDebug(ex, "Console drain failed");
        }
    }

    private static string BuildInvokeExpression(string handlerName, string argsJson)
    {
        var nameLiteral = JsonSerializer.Serialize(handlerName);
        var argsLiteral = JsonSerializer.Serialize(argsJson);

        return "(function(){var b=globalThis[Symbol.for('__cel_webview_tools')];" +
               $"if(!b||typeof b.invoke!=='function'){{return {{ok:false,error:'WebView tool bridge shim not present'}};}}" +
               $"return b.invoke({nameLiteral},{argsLiteral});}})()";
    }

    private static Result<string> UnwrapShimResult(string evalResultJson, string handlerName, ResourceKey resource)
    {
        try
        {
            using var doc = JsonDocument.Parse(evalResultJson);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Null)
            {
                return Result.Fail($"WebView tool bridge shim returned null for handler '{handlerName}' on resource '{resource}'. The shim may not have loaded.");
            }

            if (!root.TryGetProperty("ok", out var okElement))
            {
                return Result.Fail($"WebView tool bridge shim returned a malformed envelope for handler '{handlerName}' on resource '{resource}'.");
            }

            if (!okElement.GetBoolean())
            {
                var errorMessage = root.TryGetProperty("error", out var errorElement)
                    ? errorElement.GetString() ?? "unknown error"
                    : "unknown error";

                return Result.Fail($"WebView tool bridge handler '{handlerName}' failed: {errorMessage}");
            }

            if (root.TryGetProperty("value", out var valueElement))
            {
                return valueElement.GetRawText();
            }

            return "null";
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to parse WebView tool bridge response for handler '{handlerName}' on resource '{resource}': {ex.Message}")
                .WithException(ex);
        }
    }

    private async Task<string> GetUnavailableReasonAsync(ResourceKey resource)
    {
        // When the service reports the resource is unsupported, it carries a
        // specific reason (document not open, wrong editor, .webview, DevToolsBlocked).
        // Otherwise the failure is purely that no WebView is currently registered
        // (likely a timing issue), so fall back to the bridge's generic message.
        // Routed through the command queue so the underlying check (which reads
        // the documents panel) executes on the UI thread.
        var supportResult = await _commandService.ExecuteAsync<IGetWebViewToolSupportCommand, WebViewToolSupport>(
            cmd => cmd.Resource = resource);
        if (supportResult.IsSuccess)
        {
            var support = supportResult.Value;
            if (!support.IsSupported && support.Reason is not null)
            {
                return support.Reason;
            }
        }
        else
        {
            _logger.LogDebug("GetWebViewToolSupport command failed: {Error}", supportResult.FirstErrorMessage);
        }

        return $"No WebView is registered for resource '{resource}' on the webview_* tool bridge.";
    }

    private sealed class WebViewToolBridgeEntry
    {
        private readonly object _gate = new();
        private readonly Func<string, Task<string>> _evalAsync;
        private readonly Func<bool, Task> _reloadAsync;
        private readonly Func<ScreenshotRequest, Task<ScreenshotData>>? _screenshotAsync;
        private readonly List<ConsoleEntry> _consoleHistory = new();
        private readonly List<NetworkEntry> _networkHistory = new();
        private TaskCompletionSource _readyTcs;
        // Sticky failure reason set by NotifyContentFailed and cleared by the
        // next NotifyContentLoading. While set, WaitForContentReadyAsync returns
        // it directly so tool callers see a real diagnostic rather than waiting
        // through the content-ready timeout.
        private string? _failureReason;

        public WebViewToolBridgeEntry(
            Func<string, Task<string>> evalAsync,
            Func<bool, Task> reloadAsync,
            Func<ScreenshotRequest, Task<ScreenshotData>>? screenshotAsync)
        {
            _evalAsync = evalAsync;
            _reloadAsync = reloadAsync;
            _screenshotAsync = screenshotAsync;
            _readyTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Task<string> EvalAsync(string expression) => _evalAsync(expression);

        public Task ReloadAsync(bool clearCache) => _reloadAsync(clearCache);

        public bool HasScreenshotDelegate => _screenshotAsync is not null;

        public Task<ScreenshotData> ScreenshotAsync(ScreenshotRequest request)
        {
            if (_screenshotAsync is null)
            {
                throw new InvalidOperationException("No screenshot delegate registered for this WebView entry.");
            }
            return _screenshotAsync(request);
        }

        public void NotifyContentReady()
        {
            TaskCompletionSource toComplete;
            lock (_gate)
            {
                _failureReason = null;
                toComplete = _readyTcs;
            }

            toComplete.TrySetResult();
        }

        public void NotifyContentLoading()
        {
            lock (_gate)
            {
                _failureReason = null;
                if (!_readyTcs.Task.IsCompleted)
                {
                    return;
                }
                _readyTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        public void NotifyContentFailed(string reason)
        {
            TaskCompletionSource toComplete;
            lock (_gate)
            {
                _failureReason = reason;
                toComplete = _readyTcs;
            }

            // Open the gate so any pending WaitForContentReadyAsync returns
            // immediately and observes the failure reason.
            toComplete.TrySetResult();
        }

        public async Task<Result> WaitForContentReadyAsync(TimeSpan timeout)
        {
            Task readyTask;
            lock (_gate)
            {
                readyTask = _readyTcs.Task;
            }

            if (!readyTask.IsCompleted)
            {
                // Run the delay against an infinite timeout that we cancel manually
                // on the success path, so the delay task does not linger after the
                // ready signal arrives.
                using var cts = new CancellationTokenSource();
                var delayTask = Task.Delay(timeout, cts.Token);
                var completed = await Task.WhenAny(readyTask, delayTask);
                if (completed != readyTask)
                {
                    return Result.Fail($"Timed out after {timeout.TotalSeconds:0.#}s waiting for the editor's content-ready signal. The editor must call celbridge.notifyContentLoaded() (contribution editors) or finish navigation (HTML viewer) before WebView tools can dispatch.");
                }

                cts.Cancel();
            }

            string? failureReason;
            lock (_gate)
            {
                failureReason = _failureReason;
            }

            if (failureReason is not null)
            {
                return Result.Fail(failureReason);
            }

            return Result.Ok();
        }

        public void AppendConsoleEntry(ConsoleEntry consoleEntry, int historyCap)
        {
            lock (_gate)
            {
                _consoleHistory.Add(consoleEntry);
                if (_consoleHistory.Count > historyCap)
                {
                    var overflow = _consoleHistory.Count - historyCap;
                    _consoleHistory.RemoveRange(0, overflow);
                }
            }
        }

        public ConsoleSnapshot SnapshotConsole(ConsoleQueryOptions options)
        {
            List<ConsoleEntry> filtered;
            int totalCount;
            lock (_gate)
            {
                totalCount = _consoleHistory.Count;
                IEnumerable<ConsoleEntry> source = _consoleHistory;

                if (options.SinceTimestampMs.HasValue)
                {
                    var since = options.SinceTimestampMs.Value;
                    source = source.Where(entry => entry.TimestampMs > since);
                }

                if (!options.IncludeDebug)
                {
                    source = source.Where(entry => !string.Equals(entry.Level, "debug", StringComparison.OrdinalIgnoreCase));
                }

                filtered = source.ToList();
            }

            var tail = options.Tail > 0 ? options.Tail : 100;
            var taken = filtered.Count > tail
                ? filtered.GetRange(filtered.Count - tail, tail)
                : filtered;

            return new ConsoleSnapshot(taken, taken.Count, totalCount);
        }

        public void AppendNetworkEntry(NetworkEntry networkEntry, int historyCap)
        {
            lock (_gate)
            {
                _networkHistory.Add(networkEntry);
                if (_networkHistory.Count > historyCap)
                {
                    var overflow = _networkHistory.Count - historyCap;
                    _networkHistory.RemoveRange(0, overflow);
                }
            }
        }

        public NetworkSnapshot SnapshotNetwork(NetworkQueryOptions options)
        {
            List<NetworkEntry> filtered;
            int totalCount;
            lock (_gate)
            {
                totalCount = _networkHistory.Count;
                IEnumerable<NetworkEntry> source = _networkHistory;

                if (options.SinceTimestampMs.HasValue)
                {
                    var since = options.SinceTimestampMs.Value;
                    source = source.Where(entry => entry.StartTimeMs > since);
                }

                filtered = source.ToList();
            }

            var tail = options.Tail > 0 ? options.Tail : 100;
            var taken = filtered.Count > tail
                ? filtered.GetRange(filtered.Count - tail, tail)
                : filtered;

            var projected = new List<NetworkEntryView>(taken.Count);
            foreach (var entry in taken)
            {
                projected.Add(new NetworkEntryView(
                    entry.Id,
                    entry.Type,
                    entry.Method,
                    entry.Url,
                    entry.Status,
                    entry.StartTimeMs,
                    entry.DurationMs,
                    entry.RequestSize,
                    entry.ResponseSize,
                    options.IncludeHeaders ? entry.RequestHeaders : null,
                    options.IncludeHeaders ? entry.ResponseHeaders : null,
                    options.IncludeBodies ? entry.RequestBodyDescription : null,
                    options.IncludeBodies ? entry.ResponseBody : null,
                    entry.Error));
            }

            return new NetworkSnapshot(projected, projected.Count, totalCount);
        }
    }

}
