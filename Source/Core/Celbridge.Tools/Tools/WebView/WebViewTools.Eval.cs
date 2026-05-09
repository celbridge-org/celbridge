using Celbridge.Settings;
using Celbridge.WebHost;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class WebViewTools
{
    /// <summary>Evaluate an arbitrary JavaScript expression in the WebView (gated by a separate feature flag).</summary>
    [McpServerTool(Name = "webview_eval")]
    [ToolAlias("webview.eval")]
    public async partial Task<CallToolResult> Eval(string resource, string expression)
    {
        const string ToolGuide = "webview_eval";

        var webViewService = GetRequiredService<IWebViewService>();
        if (!webViewService.IsDevToolsFeatureEnabled())
        {
            return ToolResponse.FeatureFlagDisabled(FeatureFlagConstants.WebViewDevTools, "webview");
        }

        if (!webViewService.IsDevToolsEvalFeatureEnabled())
        {
            return ToolResponse.FeatureFlagDisabled(FeatureFlagConstants.WebViewDevToolsEval, "webview");
        }

        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolResponse.InvalidResourceKey(resource);
        }

        if (string.IsNullOrEmpty(expression))
        {
            return ToolResponse.Error("Expression must not be empty", ToolGuide);
        }

        // The expression body may contain sensitive output (e.g. document.cookie,
        // values fetched from storage). Log the resource and length only. The body
        // is logged at Debug for opt-in diagnostics.
        Logger.LogInformation("webview_eval resource={Resource} expressionLength={Length}", resourceKey, expression.Length);
        Logger.LogDebug("webview_eval expression={Expression}", expression);

        var toolBridge = GetRequiredService<IDocumentWebViewToolBridge>();
        var evalResult = await toolBridge.EvalAsync(resourceKey, expression);
        if (evalResult.IsFailure)
        {
            return ToolResponse.Error(evalResult, ToolGuide);
        }

        var value = evalResult.Value;
        return ToolResponse.Success(value);
    }
}
