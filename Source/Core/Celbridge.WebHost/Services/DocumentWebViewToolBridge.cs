using System.Collections.Concurrent;
using System.Text.Json;

namespace Celbridge.WebHost.Services;

/// <summary>
/// Maintains a registry of WebViews eligible for the webview_* MCP tool namespace,
/// keyed by their document resource. Routes eval and reload calls from those tools
/// to the registered WebView via the delegates supplied at registration time. The
/// delegates are responsible for marshalling onto the UI thread.
/// </summary>
public class DocumentWebViewToolBridge : IDocumentWebViewToolBridge
{
    private const string ShimRelativePath = "Celbridge.WebHost/Web/celbridge-client/core/webview-tools-shim.js";

    // Default upper bound on how long a tool call waits for the editor's content-ready
    // signal before failing. Generous enough for heavyweight editors (markdown preview,
    // Monaco) that import packages on first paint.
    private static readonly TimeSpan DefaultContentReadyTimeout = TimeSpan.FromSeconds(5);

    private readonly TimeSpan _contentReadyTimeout;

    public DocumentWebViewToolBridge() : this(DefaultContentReadyTimeout) { }

    // Test-friendly constructor so unit tests can use a short timeout without
    // waiting through the 5-second default for every gated-but-never-ready case.
    internal DocumentWebViewToolBridge(TimeSpan contentReadyTimeout)
    {
        _contentReadyTimeout = contentReadyTimeout;
    }

    // Cap accumulated console history per resource. Older entries are evicted FIFO
    // when the cap is hit. The shim has its own bounded ring; this cap protects the
    // host from a runaway editor that logs forever.
    private const int ConsoleHistoryCap = 2000;

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
            _cachedShimScript = File.ReadAllText(fullPath);
            return _cachedShimScript;
        }
    }

    public void Register(ResourceKey resource, Func<string, Task<string>> evalAsync, Func<bool, Task> reloadAsync)
    {
        var entry = new WebViewToolBridgeEntry(evalAsync, reloadAsync);
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

    public async Task<Result<string>> EvalAsync(ResourceKey resource, string expression)
    {
        if (!_entries.TryGetValue(resource, out var entry))
        {
            return Result.Fail(NoRegistrationMessage(resource));
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
            return Result.Fail(NoRegistrationMessage(resource));
        }

        // Drain the in-page console buffer into the host's accumulator before the
        // reload tears it down. Any errors logged before the reload remain readable
        // through GetConsoleAsync.
        await TryDrainConsoleAsync(entry);

        try
        {
            entry.NotifyContentLoading();
            await entry.ReloadAsync(clearCache);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"WebView reload failed for resource '{resource}': {ex.Message}")
                .WithException(ex);
        }
    }

    public async Task<Result<string>> GetConsoleAsync(ResourceKey resource, ConsoleQueryOptions options)
    {
        if (!_entries.TryGetValue(resource, out var entry))
        {
            return Result.Fail(NoRegistrationMessage(resource));
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

    private async Task<Result<string>> InvokeShimHandlerAsync(ResourceKey resource, string handlerName, object args)
    {
        if (!_entries.TryGetValue(resource, out var entry))
        {
            return Result.Fail(NoRegistrationMessage(resource));
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
        catch
        {
            // The shim may not be installed (cross-origin frame, or the page hasn't
            // navigated yet). Drain is best-effort: silently swallow rather than
            // propagating an error to the caller.
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

    private static string NoRegistrationMessage(ResourceKey resource)
    {
        return $"No tool-bridge-eligible WebView is registered for resource '{resource}'. The target must be an open document editor that permits the webview_* tools. They do not target external-URL .webview documents or packages that opt out.";
    }

    private sealed class WebViewToolBridgeEntry
    {
        private readonly object _gate = new();
        private readonly Func<string, Task<string>> _evalAsync;
        private readonly Func<bool, Task> _reloadAsync;
        private readonly List<ConsoleEntry> _consoleHistory = new();
        private TaskCompletionSource _readyTcs;

        public WebViewToolBridgeEntry(Func<string, Task<string>> evalAsync, Func<bool, Task> reloadAsync)
        {
            _evalAsync = evalAsync;
            _reloadAsync = reloadAsync;
            _readyTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Task<string> EvalAsync(string expression) => _evalAsync(expression);

        public Task ReloadAsync(bool clearCache) => _reloadAsync(clearCache);

        public void NotifyContentReady()
        {
            TaskCompletionSource toComplete;
            lock (_gate)
            {
                toComplete = _readyTcs;
            }

            toComplete.TrySetResult();
        }

        public void NotifyContentLoading()
        {
            lock (_gate)
            {
                if (!_readyTcs.Task.IsCompleted)
                {
                    return;
                }
                _readyTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        public async Task<Result> WaitForContentReadyAsync(TimeSpan timeout)
        {
            Task readyTask;
            lock (_gate)
            {
                readyTask = _readyTcs.Task;
            }

            if (readyTask.IsCompleted)
            {
                return Result.Ok();
            }

            using var cts = new CancellationTokenSource(timeout);
            var delayTask = Task.Delay(timeout, cts.Token);
            var completed = await Task.WhenAny(readyTask, delayTask);
            if (completed != readyTask)
            {
                return Result.Fail($"Timed out after {timeout.TotalSeconds:0.#}s waiting for the editor's content-ready signal. The editor must call celbridge.notifyContentLoaded() (contribution editors) or finish navigation (HTML viewer) before WebView tools can dispatch.");
            }

            cts.Cancel();
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
    }

    internal sealed record ConsoleEntry(
        string Level,
        long TimestampMs,
        IReadOnlyList<string> Args,
        string? Stack)
    {
        public static ConsoleEntry? FromJson(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var level = element.TryGetProperty("level", out var levelElement) && levelElement.ValueKind == JsonValueKind.String
                ? levelElement.GetString() ?? "log"
                : "log";

            // Date.now() is integer milliseconds, but V8's JSON serialisation
            // can emit numbers in forms that GetInt64 rejects (scientific notation
            // for very large values, fractional fallback under unusual locales).
            // Prefer TryGetInt64 and fall back to TryGetDouble so a quirky number
            // never aborts the drain.
            long timestamp = 0;
            if (element.TryGetProperty("timestampMs", out var tsElement) &&
                tsElement.ValueKind == JsonValueKind.Number)
            {
                if (!tsElement.TryGetInt64(out timestamp) &&
                    tsElement.TryGetDouble(out var tsDouble))
                {
                    timestamp = (long)tsDouble;
                }
            }

            var args = new List<string>();
            if (element.TryGetProperty("args", out var argsElement) && argsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var arg in argsElement.EnumerateArray())
                {
                    args.Add(arg.ValueKind == JsonValueKind.String ? arg.GetString() ?? string.Empty : arg.GetRawText());
                }
            }

            var stack = element.TryGetProperty("stack", out var stackElement) && stackElement.ValueKind == JsonValueKind.String
                ? stackElement.GetString()
                : null;

            return new ConsoleEntry(level, timestamp, args, stack);
        }
    }

    internal sealed record ConsoleSnapshot(
        IReadOnlyList<ConsoleEntry> Entries,
        int Returned,
        int TotalAccumulated);
}
