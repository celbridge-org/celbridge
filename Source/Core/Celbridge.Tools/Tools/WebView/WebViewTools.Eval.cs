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
        var webViewService = GetRequiredService<IWebViewService>();
        if (!webViewService.IsDevToolsFeatureEnabled())
        {
            return ToolError($"The '{FeatureFlagConstants.WebViewDevTools}' feature flag is disabled. Enable it in the user .celbridge config to use the webview_* tools.");
        }

        if (!webViewService.IsDevToolsEvalFeatureEnabled())
        {
            return ToolError($"The '{FeatureFlagConstants.WebViewDevToolsEval}' feature flag is disabled. webview_eval is gated separately because it is an arbitrary code execution primitive.");
        }

        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ToolError($"Invalid resource key: '{resource}'");
        }

        if (string.IsNullOrEmpty(expression))
        {
            return ToolError("Expression must not be empty");
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
            return ToolError(evalResult);
        }

        var value = evalResult.Value;
        return ToolSuccess(value);
    }
}
