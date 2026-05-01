using Celbridge.Settings;
using Celbridge.WebHost;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Celbridge.Tools;

public partial class WebViewTools
{
    /// <summary>
    /// Evaluates a JavaScript expression in the WebView hosting the document.
    /// The expression must produce a JSON-serialisable value. The result is
    /// returned as a JSON string. Requires the webview-dev-tools and
    /// webview-dev-tools-eval feature flags. If the target document is not
    /// eligible (wrong editor, external URL, or package opts out) the error
    /// message names the specific reason.
    /// </summary>
    /// <param name="resource">Resource key of the open document whose WebView to target.</param>
    /// <param name="expression">JavaScript expression to evaluate. Must be a single expression — the returned value is the expression's JSON-serialised result. Multi-statement code (const/let declarations, if/else blocks, several statements in sequence) returns null because statements have no value. Wrap such code in an IIFE with an explicit return, for example: (function() { const x = f(); return x + 1; })()</param>
    /// <returns>JSON-encoded value produced by the expression. Returns "null" when the expression evaluates to undefined or null.</returns>
    [McpServerTool(Name = "webview_eval")]
    [ToolAlias("webview.eval")]
    public async partial Task<CallToolResult> Eval(string resource, string expression)
    {
        var webViewService = GetRequiredService<IWebViewService>();
        if (!webViewService.IsDevToolsFeatureEnabled())
        {
            return ErrorResult($"The '{FeatureFlagConstants.WebViewDevTools}' feature flag is disabled. Enable it in the user .celbridge config to use the webview_* tools.");
        }

        if (!webViewService.IsDevToolsEvalFeatureEnabled())
        {
            return ErrorResult($"The '{FeatureFlagConstants.WebViewDevToolsEval}' feature flag is disabled. webview_eval is gated separately because it is an arbitrary code execution primitive.");
        }

        if (!ResourceKey.TryCreate(resource, out var resourceKey))
        {
            return ErrorResult($"Invalid resource key: '{resource}'");
        }

        if (string.IsNullOrEmpty(expression))
        {
            return ErrorResult("Expression must not be empty");
        }

        Logger.LogInformation("webview_eval resource={Resource} expression={Expression}", resourceKey, expression);

        var toolBridge = GetRequiredService<IDocumentWebViewToolBridge>();
        var evalResult = await toolBridge.EvalAsync(resourceKey, expression);
        if (evalResult.IsFailure)
        {
            return ErrorResult(evalResult.FirstErrorMessage);
        }

        var value = evalResult.Value;
        return SuccessResult(value);
    }
}
