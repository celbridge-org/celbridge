using System.Collections.Concurrent;

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

    public async Task<Result<string>> EvalAsync(ResourceKey resource, string expression)
    {
        if (!_entries.TryGetValue(resource, out var entry))
        {
            return Result.Fail($"No tool-bridge-eligible WebView is registered for resource '{resource}'. The target must be an open document editor that permits the webview_* tools. They do not target external-URL .webview documents or packages that opt out.");
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
            return Result.Fail($"No tool-bridge-eligible WebView is registered for resource '{resource}'. The target must be an open document editor that permits the webview_* tools. They do not target external-URL .webview documents or packages that opt out.");
        }

        try
        {
            await entry.ReloadAsync(clearCache);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail($"WebView reload failed for resource '{resource}': {ex.Message}")
                .WithException(ex);
        }
    }

    private sealed record WebViewToolBridgeEntry(Func<string, Task<string>> EvalAsync, Func<bool, Task> ReloadAsync);
}
