using Celbridge.Settings;
using Celbridge.WebHost;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class WebViewTools
{
    /// <summary>
    /// Reloads the WebView hosting the document. Page state is discarded by design.
    /// The editor's package code reinitialises from scratch. Use this after writing
    /// new package files (HTML, CSS, JS, manifest) so the WebView picks up the edits.
    /// Requires the webview-dev-tools feature flag. Works on any open document editor
    /// (text, markdown, HTML viewer, custom editors). Not available for external-URL
    /// `.webview` documents or for editors whose package opts out of devtools —
    /// call the tool and read the error if uncertain.
    /// </summary>
    /// <param name="resource">Resource key of the open document whose WebView to reload.</param>
    /// <param name="clearCache">When true (default), clears the WebView's HTTP cache (in-memory and on-disk) before reloading so newly-edited package sub-resources (JS, CSS, images) are refetched instead of served stale. The cache clear evicts data for every document hosted in the same WebView profile, not just this one — pass false to skip the clear when you know no sub-resources have changed and want a slightly faster reload.</param>
    /// <returns>"ok" on success, or an error message if the WebView is not registered or reload failed.</returns>
    [McpServerTool(Name = "webview_reload")]
    [ToolAlias("webview.reload")]
    public async partial Task<CallToolResult> Reload(string resource, bool clearCache = true)
    {
        var webViewService = GetRequiredService<IWebViewService>();
        if (!webViewService.IsDevToolsFeatureEnabled())
        {
            return ErrorResult($"The '{FeatureFlagConstants.WebViewDevTools}' feature flag is disabled. Enable it in the user .celbridge config to use the webview_* tools.");
        }

        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ErrorResult($"Invalid resource key: '{resource}'");
        }

        Logger.LogInformation("webview_reload resource={Resource} clearCache={ClearCache}", resourceKey, clearCache);

        var toolBridge = GetRequiredService<IDocumentWebViewToolBridge>();
        var reloadResult = await toolBridge.ReloadAsync(resourceKey, clearCache);
        if (reloadResult.IsFailure)
        {
            return ErrorResult(reloadResult.FirstErrorMessage);
        }

        return SuccessResult("ok");
    }
}
